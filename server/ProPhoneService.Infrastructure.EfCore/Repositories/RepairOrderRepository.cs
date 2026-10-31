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
    public async Task<IReadOnlyList<RepairOrder>> GetPendingByEmailAsync(
        string contactEmail,
        CancellationToken cancellationToken = default)
    {
        // Без AsNoTracking: после подтверждения кодом заявки привязываются к клиенту
        return await Set
            .Where(x => x.Status == RepairStatus.PendingContact && x.ContactEmail == contactEmail)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepairOrder>> GetAllByClientIdAsync(
        Guid clientId,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(skip, take);

        return await Set
            .AsNoTracking()
            .Where(x => x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
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
}
