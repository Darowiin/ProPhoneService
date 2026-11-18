namespace ProPhoneService.Domain.Shared.Enum;

/// <summary>
/// Статус заказа на ремонт
/// </summary>
public enum RepairStatus
{
    /// <summary>
    /// Заявка принята в обработку
    /// </summary>
    Created,

    /// <summary>
    /// Ремонт в работе
    /// </summary>
    InProgress,

    /// <summary>
    /// Устройство готово к выдаче после ремонта или без ремонта
    /// </summary>
    Ready,

    /// <summary>
    /// Заказ закрыт, устройство выдано клиенту
    /// </summary>
    Completed,

    /// <summary>
    /// Заказ отменён клиентом или мастером
    /// </summary>
    Cancelled,

    /// <summary>
    /// Заявка оставлена без входа: ждёт подтверждения контакта, клиент и аккаунт ещё не созданы
    /// </summary>
    PendingContact,
}
