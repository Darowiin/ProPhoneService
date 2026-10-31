using ProPhoneService.Domain.Model;

namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Репозиторий одноразовых кодов подтверждения
/// </summary>
public interface IAuthCodeRepository : IRepository<AuthCode>
{
    /// <summary>
    /// Найти активную запись кода по email или <c>null</c>, если не запрашивался
    /// </summary>
    Task<AuthCode?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
}
