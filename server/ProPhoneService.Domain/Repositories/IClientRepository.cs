using ProPhoneService.Domain.Model;

namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Репозиторий клиентов мастерской
/// </summary>
public interface IClientRepository : IRepository<Client>
{
    /// <summary>
    /// Найти клиента по телефону или <c>null</c>, если не найден
    /// </summary>
    /// <remarks>Используется при создании заявки (найти-или-создать) и будет переиспользован для логина по телефону</remarks>
    Task<Client?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);

}
