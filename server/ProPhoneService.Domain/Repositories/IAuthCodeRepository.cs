using ProPhoneService.Domain.Model;

namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Репозиторий одноразовых кодов подтверждения
/// </summary>
public interface IAuthCodeRepository : IRepository<AuthCode>
{
    /// <summary>
    /// Выполнить операцию в транзакции, сериализуя запросы для одного телефона
    /// </summary>
    Task<T> ExecuteForPhoneAsync<T>(string phone, Func<Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Найти активную запись кода по телефону или <c>null</c>, если не запрашивался
    /// </summary>
    Task<AuthCode?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);
}
