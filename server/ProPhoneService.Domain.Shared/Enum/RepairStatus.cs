namespace ProPhoneService.Domain.Shared.Enum;

/// <summary>
/// Статус заказа на ремонт
/// </summary>
public enum RepairStatus
{
    /// <summary>
    /// Заказ создан, ожидает диагностики
    /// </summary>
    Created,

    /// <summary>
    /// Диагностика устройства в работе, стоимость и объём работ ещё не определены
    /// </summary>
    InDiagnostic,

    /// <summary>
    /// Стоимость и объём работ согласованы с клиентом
    /// </summary>
    Agreed,

    /// <summary>
    /// Ремонт в работе
    /// </summary>
    InProgress,

    /// <summary>
    /// Ремонт завершён, устройство готово к выдаче
    /// </summary>
    Ready,

    /// <summary>
    /// Заказ закрыт, устройство выдано клиенту
    /// </summary>
    Completed,

    /// <summary>
    /// Заказ отменён клиентом или мастером
    /// </summary>
    Cancelled
}
