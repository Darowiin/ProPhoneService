using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Статистика дашборда мастера: выданные заказы за период (месяц, квартал, всё время)
/// </summary>
public class StatisticsService(IRepairOrderRepository orderRepository, TimeProvider timeProvider)
{
    /// <summary>
    /// Получить статистику выданных заказов за период
    /// </summary>
    /// <param name="period">Период: с начала месяца, с начала квартала или всё время</param>
    public async Task<StatsDto> GetAsync(StatsPeriod period, CancellationToken cancellationToken = default)
    {
        var (fromUtc, toUtc) = ResolveRange(period, timeProvider.GetUtcNow().UtcDateTime);

        var counts = await orderRepository.CountCompletedByDeviceTypeAsync(fromUtc, toUtc, cancellationToken);
        var total = await orderRepository.SumCompletedOrdersAsync(fromUtc, toUtc, cancellationToken);

        var byDeviceType = counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Select(x => new DeviceTypeStatsDto(x.Key, x.Value))
            .ToList();

        return new StatsDto(fromUtc, toUtc, total, byDeviceType);
    }

    /// <summary>
    /// Границы периода в UTC: начало месяца/квартала, конец - текущий момент
    /// </summary>
    private static (DateTime? FromUtc, DateTime ToUtc) ResolveRange(StatsPeriod period, DateTime nowUtc)
    {
        return period switch
        {
            StatsPeriod.Month => (new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc), nowUtc),
            StatsPeriod.Quarter => (
                new DateTime(nowUtc.Year, (nowUtc.Month - 1) / 3 * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc),
                nowUtc),
            StatsPeriod.All => (null, nowUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Неизвестный период статистики"),
        };
    }
}
