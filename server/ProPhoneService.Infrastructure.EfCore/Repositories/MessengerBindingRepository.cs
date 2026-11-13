using Microsoft.EntityFrameworkCore;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Infrastructure.EfCore.Repositories;

/// <summary>
/// Постоянное хранение привязок, общее для экземпляров API
/// </summary>
public class MessengerBindingRepository(ProPhoneServiceDbContext context)
    : EfRepository<MessengerBinding>(context), IMessengerBindingRepository
{
    /// <inheritdoc />
    public Task<MessengerBinding?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        Set.AsNoTracking().FirstOrDefaultAsync(x => x.Channel == MessengerChannel.Vk && x.TokenHash == tokenHash, cancellationToken);

    /// <inheritdoc />
    public Task<MessengerBinding?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
        Set.AsNoTracking().Where(x => x.Channel == MessengerChannel.Vk && x.Phone == phone && x.RecipientId != null && x.LinkedAt != null)
            .OrderByDescending(x => x.AccountId != null).ThenByDescending(x => x.LinkedAt)
            .ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<MessengerBinding?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Set.AsNoTracking().Where(x => x.Channel == MessengerChannel.Vk && x.OrderId == orderId
            && x.RecipientId != null && x.LinkedAt != null)
            .OrderByDescending(x => x.LinkedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryBindAsync(string tokenHash, MessengerChannel channel, string recipientId,
        DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        var binding = await GetByTokenHashAsync(tokenHash, cancellationToken);

        if (binding is null || binding.Channel != channel || binding.RecipientId != null || binding.ExpiresAt <= nowUtc)
        {
            return false;
        }

        // Защищаем контакт от перепривязки через новую гостевую заявку с чужим телефоном
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({binding.Phone}, 1))", cancellationToken);

        var existing = await GetByPhoneAsync(binding.Phone, cancellationToken);

        if (binding.AccountId is null && existing is not null && (existing.Channel != channel || existing.RecipientId != recipientId))
        {
            return false;
        }

        var changed = await Set.Where(x => x.TokenHash == tokenHash && x.Channel == channel
                && x.RecipientId == null && x.ExpiresAt > nowUtc
                && (x.OrderId == null || Context.RepairOrders.Any(order => order.Id == x.OrderId
                    && order.Status == RepairStatus.PendingContact)))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RecipientId, recipientId)
                .SetProperty(x => x.LinkedAt, nowUtc), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return changed == 1;
    }
}
