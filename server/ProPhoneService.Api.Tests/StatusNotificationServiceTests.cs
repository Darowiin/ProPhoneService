using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class StatusNotificationServiceTests
{
    [Fact]
    public async Task LogFailure_DoesNotFailAlreadySavedStatus()
    {
        var order = RepairOrder.Create(Guid.NewGuid(), Guid.NewGuid());
        var notifier = new Mock<INotifier>();
        notifier.Setup(x => x.SendAsync(MessengerChannel.Vk, "42", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var bindings = new Mock<IMessengerBindingRepository>();
        bindings.Setup(x => x.GetByClientIdAsync(order.ClientId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessengerBinding
            {
                Id = Guid.NewGuid(), TokenHash = "hash", Phone = "+79001234567", Channel = MessengerChannel.Vk,
                RecipientId = "42", LinkedAt = DateTime.UtcNow, AccountId = Guid.NewGuid(),
            });
        var logs = new Mock<IRepository<NotificationLog>>();
        logs.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("db unavailable"));
        var service = new StatusNotificationService(notifier.Object, logs.Object, new NotificationOptions(),
            NullLogger<StatusNotificationService>.Instance, bindings.Object);

        await service.NotifyStatusChangedAsync(order);

        Assert.Equal(RepairStatus.Created, order.Status);
        notifier.Verify(x => x.SendAsync(MessengerChannel.Vk, "42", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagatedWithoutRetries()
    {
        var order = RepairOrder.Create(Guid.NewGuid(), Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();
        var notifier = new Mock<INotifier>();
        notifier.Setup(x => x.SendAsync(MessengerChannel.Vk, "42", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => { cancellation.Cancel(); return Task.FromCanceled(cancellation.Token); });
        var bindings = new Mock<IMessengerBindingRepository>();
        bindings.Setup(x => x.GetByClientIdAsync(order.ClientId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessengerBinding
            {
                Id = Guid.NewGuid(), TokenHash = "hash", Phone = "+79001234567", Channel = MessengerChannel.Vk,
                RecipientId = "42", LinkedAt = DateTime.UtcNow, AccountId = Guid.NewGuid(),
            });
        var logs = new Mock<IRepository<NotificationLog>>(MockBehavior.Strict);
        var service = new StatusNotificationService(notifier.Object, logs.Object, new NotificationOptions(),
            NullLogger<StatusNotificationService>.Instance, bindings.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.NotifyStatusChangedAsync(order, cancellation.Token));

        notifier.Verify(x => x.SendAsync(MessengerChannel.Vk, "42", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        logs.VerifyNoOtherCalls();
    }
}
