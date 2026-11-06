using ProPhoneService.Domain.Model;

namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Репозиторий аккаунтов
/// </summary>
public interface IAccountRepository : IRepository<Account>
{
    /// <summary>
    /// Найти аккаунт по идентификатору клиента или <c>null</c>, если не найден
    /// </summary>
    Task<Account?> GetByClientIdAsync(Guid clientId, CancellationToken cancellationToken = default);
}
