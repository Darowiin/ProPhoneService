using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Утверждённые тексты сообщений VK с подстановкой кода и номера заявки
/// </summary>
public static class NotificationMessages
{
    /// <summary>
    /// Текст успешной привязки мессенджера
    /// </summary>
    public const string MessengerLinked = "Мессенджер успешно привязан!\n"
        + "Ваш аккаунт успешно привязан к профилю на сайте.\n"
        + "Теперь вы можете получать уведомления о состоянии ваших заявок через этот мессенджер.";

    /// <summary>
    /// Подставить одноразовый код в утверждённый текст
    /// </summary>
    public static string ConfirmationCode(string code) =>
        $"Ваш код подтверждения: {code}\nСрок действия кода — 5 минут. Не передавайте его третьим лицам.";

    /// <summary>
    /// Получить заголовок и текст уведомления или null для статуса без отправки
    /// </summary>
    public static (string Subject, string Text)? Status(RepairStatus? status, string orderNumber)
    {
        var body = status switch
        {
            RepairStatus.Created => $"Ваша заявка №{orderNumber} успешно принята!\n"
                + "Мы приступили к её обработке. Вы получите уведомление, когда статус заявки изменится.",
            RepairStatus.InProgress => $"По вашей заявке №{orderNumber} начались ремонтные работы.\n"
                + "Мы сообщим вам, когда устройство будет готово к выдаче.",
            RepairStatus.Ready => $"Ваша заявка №{orderNumber} выполнена.\n"
                + "Устройство готово к выдаче. Вы можете забрать его в нашем сервисе.\nБудем рады вас видеть!",
            RepairStatus.Cancelled => $"Ваша заявка №{orderNumber} была отменена.\n"
                + "Если у вас возникли вопросы, свяжитесь с нами или обратитесь в наш пункт обслуживания.",
            _ => null,
        };

        if (body is null)
        {
            return null;
        }

        var subject = RepairStatusNames.GetName(status);
        return (subject, subject + "\n" + body);
    }
}
