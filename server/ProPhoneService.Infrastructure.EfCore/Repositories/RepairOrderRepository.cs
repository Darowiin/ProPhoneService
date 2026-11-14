using Microsoft.EntityFrameworkCore;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Domain.Repositories;

namespace ProPhoneService.Infrastructure.EfCore.Repositories;

/// <summary>
/// EF Core реализация <see cref="IRepairOrderRepository"/>
/// </summary>
public class RepairOrderRepository(ProPhoneServiceDbContext context)
    : EfRepository<RepairOrder>(context), IRepairOrderRepository
{
    /// <inheritdoc />
    public async Task<RepairOrder?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Без AsNoTracking: заказ загружается для изменения статуса
        return await Set
            .Include(x => x.StatusHistory)
            .Include(x => x.Services)
                .ThenInclude(x => x.Service)
            .Include(x => x.Device)
            .Include(x => x.Client)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepairOrder>> GetPageByClientIdAsync(
        Guid clientId,
        RepairStatus? status,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(skip, take);

        return await Set
            .AsNoTracking()
            .Include(x => x.Device)
            .Include(x => x.StatusHistory)
            .Where(x => x.ClientId == clientId)
            // Неподтверждённая заявка в ЛК не показывается
            .Where(x => x.Status == null || x.Status != RepairStatus.PendingContact)
            .Where(x => status == null || x.Status == status)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<RepairOrder?> GetForStatusChangeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Без AsNoTracking: заказ меняется, история статуса дописывается; клиент нужен для уведомления
        return Set
            .Include(x => x.Client)
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<DeviceType, int>> CountCompletedByDeviceTypeAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await CompletedOrders(fromUtc, toUtc)
            .Select(order => order.Device!.Type)
            .GroupBy(type => type)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Type, x => x.Count);
    }

    /// <inheritdoc />
    public Task<decimal> SumCompletedOrdersAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default)
    {
        return CompletedOrders(fromUtc, toUtc)
            .Select(order => order.TotalPrice)
            .SumAsync(cancellationToken);
    }

    private IQueryable<RepairOrder> CompletedOrders(DateTime? fromUtc, DateTime? toUtc)
    {
        return Set.Where(order => order.Status == RepairStatus.Completed)
            .Where(order => order.StatusHistory.Any(history => history.Status == RepairStatus.Completed
                && (fromUtc == null || history.ChangedAt >= fromUtc)
                && (toUtc == null || history.ChangedAt <= toUtc)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepairOrder>> GetPageAsync(
        RepairStatus? status,
        DateTime? visitFrom,
        DateTime? visitTo,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(skip, take);

        return await Set
            .AsNoTracking()
            .Include(x => x.Device)
            .Include(x => x.StatusHistory)
            // Неподтверждённая заявка в панель мастера не попадает
            .Where(x => x.Status == null || x.Status != RepairStatus.PendingContact)
            .Where(x => status == null || x.Status == status)
            .Where(x => visitFrom == null || x.PreferredVisitAt >= visitFrom)
            .Where(x => visitTo == null || x.PreferredVisitAt <= visitTo)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CancelExpiredPendingAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var cutoff = nowUtc.AddMinutes(-5);

        // Условное обновление и история атомарны; xmin защищает параллельное подтверждение
        return Context.Database.ExecuteSqlInterpolatedAsync($"""
            WITH cancelled AS (
                UPDATE repair_order SET status = 'Cancelled'
                WHERE status = 'PendingContact' AND created_at <= {cutoff}
                RETURNING id
            )
            INSERT INTO repair_status_history (id, repair_order_id, status, changed_at, changed_by, comment)
            SELECT gen_random_uuid(), id, 'Cancelled', {nowUtc}, NULL, NULL FROM cancelled
            """, cancellationToken);
    }
}
