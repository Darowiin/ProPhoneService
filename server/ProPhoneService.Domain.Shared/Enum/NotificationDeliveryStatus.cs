namespace ProPhoneService.Domain.Shared.Enum;

/// <summary>
/// Статус доставки уведомления клиенту
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>
    /// Провайдер принял сообщение
    /// </summary>
    Sent,

    /// <summary>
    /// Все попытки отправки исчерпаны, сообщение не доставлено
    /// </summary>
    Failed,
}
