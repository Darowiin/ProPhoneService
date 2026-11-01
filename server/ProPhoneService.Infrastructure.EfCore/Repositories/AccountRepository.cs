using Microsoft.EntityFrameworkCore;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;

namespace ProPhoneService.Infrastructure.EfCore.Repositories;

/// <summary>
/// EF Core реализация <see cref="IAccountRepository"/>
/// </summary>
public class AccountRepository(ProPhoneServiceDbContext context)
    : EfRepository<Account>(context), IAccountRepository
{
    /// <inheritdoc />
    public Task<Account?> GetByClientIdAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        return Set.AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == clientId, cancellationToken);
    }
}
