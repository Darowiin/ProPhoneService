using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class NotificationMessagesTests
{
    [Theory]
    [InlineData(RepairStatus.Created, "Заявка принята", "Заявка принята\nВаша заявка №З-000123 успешно принята!\nМы приступили к её обработке. Вы получите уведомление, когда статус заявки изменится.")]
    [InlineData(RepairStatus.InProgress, "В ремонте", "В ремонте\nПо вашей заявке №З-000123 начались ремонтные работы.\nМы сообщим вам, когда устройство будет готово к выдаче.")]
    [InlineData(RepairStatus.Ready, "Готов к выдаче", "Готов к выдаче\nВаша заявка №З-000123 выполнена.\nУстройство готово к выдаче. Вы можете забрать его в нашем сервисе.\nБудем рады вас видеть!")]
    [InlineData(RepairStatus.Cancelled, "Заявка отменена", "Заявка отменена\nВаша заявка №З-000123 была отменена.\nЕсли у вас возникли вопросы, свяжитесь с нами или обратитесь в наш пункт обслуживания.")]
    public void StatusMessages_MatchApprovedTextAndSubstituteOrderNumber(RepairStatus status, string title, string expected)
    {
        const string number = "З-000123";

        var result = NotificationMessages.Status(status, number);

        Assert.NotNull(result);
        Assert.Equal(title, result.Value.Subject);
        Assert.Equal(expected, result.Value.Text);
        Assert.DoesNotContain("{order_number}", result.Value.Text);
        Assert.DoesNotContain(status.ToString(), result.Value.Text);
    }

    [Fact]
    public void CodeMessage_MatchesApprovedText()
    {
        const string code = "012345";

        var result = NotificationMessages.ConfirmationCode(code);

        Assert.Equal("Ваш код подтверждения: 012345\nСрок действия кода — 5 минут. Не передавайте его третьим лицам.", result);
        Assert.DoesNotContain("{code}", result);
    }

    [Theory]
    [InlineData(RepairStatus.Completed)]
    [InlineData(RepairStatus.PendingContact)]
    public async Task StatusWithoutMessage_DoesNotCallProviderOrCreateLog(RepairStatus status)
    {
        var notifier = new Mock<INotifier>(MockBehavior.Strict);
        var logs = new Mock<IRepository<NotificationLog>>(MockBehavior.Strict);
        var service = new StatusNotificationService(notifier.Object, logs.Object, new NotificationOptions(),
            NullLogger<StatusNotificationService>.Instance, Mock.Of<IMessengerBindingRepository>());
        var order = status == RepairStatus.PendingContact
            ? RepairOrder.CreatePending(Guid.NewGuid(), "+79001234567")
            : RepairOrder.Create(Guid.NewGuid(), Guid.NewGuid());
        if (status == RepairStatus.Completed)
        {
            order.ChangeStatus(RepairStatus.InProgress);
            order.ChangeStatus(RepairStatus.Ready);
            order.ChangeStatus(RepairStatus.Completed);
        }

        await service.NotifyStatusChangedAsync(order);

        notifier.VerifyNoOtherCalls();
        logs.VerifyNoOtherCalls();
        Assert.Equal("Выдан клиенту", RepairStatusNames.GetName(RepairStatus.Completed));
    }
}
