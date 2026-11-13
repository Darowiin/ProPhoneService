using System.Security.Cryptography;
using System.Text;
using Moq;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class MessengerBindingTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => _now; }

    private sealed class Harness
    {
        public Mock<IMessengerBindingRepository> Bindings { get; } = new();
        public Mock<IRepairOrderRepository> Orders { get; } = new();
        public MessengerBindingService Service => new(Bindings.Object, Orders.Object, Mock.Of<IAccountRepository>(),
            Mock.Of<IClientRepository>(), MessengerDeliveryTests.Options(), new Clock());
    }

    [Theory]
    [InlineData(MessengerChannel.Vk, "https://vk.me/club123?ref=")]
    public async Task CreateLink_PersistsHashAndExpiresWithPendingOrder(MessengerChannel channel, string prefix)
    {
        var harness = new Harness();
        var order = RepairOrder.CreatePending(Guid.NewGuid(), "+79001234567");
        order.CreatedAt = _now.UtcDateTime.AddMinutes(-1);
        harness.Orders.Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        MessengerBinding? saved = null;
        harness.Bindings.Setup(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()))
            .Callback((MessengerBinding value, CancellationToken _) => saved = value).Returns(Task.CompletedTask);

        var response = await harness.Service.CreateForOrderAsync(order.Id, channel);

        Assert.NotNull(response);
        Assert.StartsWith(prefix, response.Url);
        var token = response.Url[prefix.Length..];
        Assert.Equal(64, token.Length);
        Assert.Equal(order.CreatedAt.AddMinutes(5), response.ExpiresAtUtc);
        Assert.NotNull(saved);
        Assert.NotEqual(token, saved.TokenHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), saved.TokenHash);
        Assert.Equal(order.Id, saved.OrderId);
        Assert.Equal(order.ContactPhone, saved.Phone);
    }

    [Fact]
    public async Task ExpiredOrder_DoesNotCreateLink()
    {
        var harness = new Harness();
        var order = RepairOrder.CreatePending(Guid.NewGuid(), "+79001234567");
        order.CreatedAt = _now.UtcDateTime.AddMinutes(-5);
        harness.Orders.Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await harness.Service.CreateForOrderAsync(order.Id, MessengerChannel.Vk);

        Assert.Null(result);
        harness.Bindings.Verify(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData((MessengerChannel)0, 1, null)]
    [InlineData(MessengerChannel.Vk, 0, null)]
    [InlineData(MessengerChannel.Vk, 1, "2")]
    public async Task WrongChannelExpiredOrOtherRecipient_CannotBind(MessengerChannel channel, int minutes, string? existingRecipient)
    {
        var harness = new Harness();
        var binding = new MessengerBinding { Id = Guid.NewGuid(), TokenHash = "hash", Phone = "+79001234567",
            Channel = MessengerChannel.Vk, ExpiresAt = _now.UtcDateTime.AddMinutes(minutes), RecipientId = existingRecipient };
        harness.Bindings.Setup(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(binding);

        var result = await harness.Service.BindAsync(channel, Token, 42);

        Assert.False(result);
        harness.Bindings.Verify(x => x.TryBindAsync(It.IsAny<string>(), It.IsAny<MessengerChannel>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateCallbackFromSameRecipient_IsIdempotent()
    {
        var harness = new Harness();
        harness.Bindings.Setup(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessengerBinding { Id = Guid.NewGuid(), TokenHash = "hash", Phone = "+79001234567",
                Channel = MessengerChannel.Vk, ExpiresAt = _now.UtcDateTime.AddMinutes(1), RecipientId = "42" });

        var result = await harness.Service.BindAsync(MessengerChannel.Vk, Token, 42);

        Assert.True(result);
        harness.Bindings.Verify(x => x.TryBindAsync(It.IsAny<string>(), It.IsAny<MessengerChannel>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
