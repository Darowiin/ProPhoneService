namespace ProPhoneService.Application.Services;

/// <summary>
/// Хранилище refresh-токенов: выдача, однократное погашение (ротация)
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Выдать refresh-токен для аккаунта с указанным сроком жизни
    /// </summary>
    string Issue(Guid accountId, DateTimeOffset expiresAtUtc);

    /// <summary>
    /// Погасить токен (ротация) и вернуть идентификатор аккаунта;
    /// <c>null</c>, если токен не найден, уже погашен (повторное использование) или истёк
    /// </summary>
    Guid? TryConsume(string? token);
}
