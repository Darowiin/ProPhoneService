using Microsoft.EntityFrameworkCore;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;

namespace ProPhoneService.Infrastructure.EfCore.Repositories;

/// <summary>
/// EF Core реализация <see cref="IClientRepository"/>
/// </summary>
public class ClientRepository(ProPhoneServiceDbContext context) : EfRepository<Client>(context), IClientRepository
{
    /// <inheritdoc />
    public Task<Client?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        return Set.FirstOrDefaultAsync(x => x.Phone == phone, cancellationToken);
    }

}
