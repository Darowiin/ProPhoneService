using Microsoft.EntityFrameworkCore;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;

namespace ProPhoneService.Infrastructure.EfCore.Repositories;

/// <summary>
/// EF Core реализация <see cref="IAuthCodeRepository"/>
/// </summary>
public class AuthCodeRepository(ProPhoneServiceDbContext context)
    : EfRepository<AuthCode>(context), IAuthCodeRepository
{
    /// <inheritdoc />
    public async Task<T> ExecuteForPhoneAsync<T>(string phone, Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({phone}, 0))", cancellationToken);
        var result = await operation();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public Task<AuthCode?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        // Без AsNoTracking: запись будет обновляться (счётчик попыток) или удаляться (одноразовый код)
        return Set.FirstOrDefaultAsync(x => x.Phone == phone, cancellationToken);
    }
}
