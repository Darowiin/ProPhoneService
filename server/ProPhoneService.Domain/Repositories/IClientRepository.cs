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
    public Task<Client?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Найти клиента по email или <c>null</c>, если не найден
    /// </summary>
    /// <remarks>Заложен для будущего логина по email с кодом на почту</remarks>
    public Task<Client?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
}
