namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Статистика дашборда мастера: выданные заказы за период
/// </summary>
/// <param name="FromUtc">Начало периода (UTC; <c>null</c> - всё время)</param>
/// <param name="ToUtc">Конец периода (UTC, включительно)</param>
/// <param name="CompletedTotalPrice">Сумма по завершённым заказам, руб</param>
/// <param name="CompletedByDeviceType">Количество выданных заказов по типам устройств</param>
public record StatsDto(
    DateTime? FromUtc,
    DateTime? ToUtc,
    decimal CompletedTotalPrice,
    IReadOnlyList<DeviceTypeStatsDto> CompletedByDeviceType);
