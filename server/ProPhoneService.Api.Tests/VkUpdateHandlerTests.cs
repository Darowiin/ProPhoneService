using System.Text.Json;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class VkUpdateHandlerTests
{
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Theory]
    [InlineData(123, 42, 42, true)]
    [InlineData(124, 42, 42, false)]
    [InlineData(123, 42, 43, false)]
    [InlineData(123, -42, -42, false)]
    public async Task OnlyPrivateMessageFromExpectedCommunityCanBind(long groupId, long from, long peer, bool expected)
    {
        var repo = new Mock<IMessengerBindingRepository>();
        repo.Setup(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessengerBinding { Id = Guid.NewGuid(), Phone = "+79001234567", TokenHash = "hash",
                AccountId = Guid.NewGuid(), Channel = MessengerChannel.Vk, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        repo.Setup(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Vk, "42", It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var notifier = new Mock<INotifier>();
        notifier.Setup(x => x.SendAsync(It.IsAny<MessengerChannel>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var options = MessengerDeliveryTests.Options();
        var bindings = new MessengerBindingService(repo.Object, Mock.Of<IRepairOrderRepository>(),
            Mock.Of<IAccountRepository>(), Mock.Of<IClientRepository>(), options, TimeProvider.System);
        var handler = new VkUpdateHandler(bindings, notifier.Object, options);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "message_new", group_id = groupId,
            @object = new { message = new { from_id = from, peer_id = peer, @ref = Token } },
        }));

        await handler.HandleAsync(json.RootElement);

        repo.Verify(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Vk, "42", It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), expected ? Times.Once() : Times.Never());
        notifier.Verify(x => x.SendAsync(MessengerChannel.Vk, "42",
            It.Is<string>(s => s.StartsWith("Мессенджер успешно привязан!")), It.IsAny<CancellationToken>()),
            expected ? Times.Once() : Times.Never());
    }
}
