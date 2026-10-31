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
    public Task<AuthCode?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // Без AsNoTracking: запись будет обновляться (счётчик попыток) или удаляться (одноразовый код)
        return Set.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
    }
}
