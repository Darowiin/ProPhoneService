using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Хранение и атомарное погашение ссылок привязки мессенджера
/// </summary>
public interface IMessengerBindingRepository : IRepository<MessengerBinding>
{
    /// <summary>
    /// Найти привязку по хэшу токена
    /// </summary>
    Task<MessengerBinding?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Найти актуальную привязку подтверждённого контакта
    /// </summary>
    Task<MessengerBinding?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Найти последнюю привязку конкретной заявки
    /// </summary>
    Task<MessengerBinding?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Привязать получателя, только если ссылка ещё не использована и не истекла
    /// </summary>
    Task<bool> TryBindAsync(string tokenHash, MessengerChannel channel, string recipientId,
        DateTime nowUtc, CancellationToken cancellationToken = default);
}
