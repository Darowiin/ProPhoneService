namespace ProPhoneService.Domain.Shared.Enum;

/// <summary>
/// Период статистики для дашборда мастера
/// </summary>
public enum StatsPeriod
{
    /// <summary>
    /// С начала текущего месяца
    /// </summary>
    Month,

    /// <summary>
    /// С начала текущего квартала
    /// </summary>
    Quarter,

    /// <summary>
    /// Всё время
    /// </summary>
    All,
}
