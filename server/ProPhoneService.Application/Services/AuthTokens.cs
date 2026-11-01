using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Выданная авторизационная сессия: access-токен и refresh-токен
/// </summary>
/// <param name="AccountId">Идентификатор аккаунта</param>
/// <param name="ClientId">Идентификатор клиента</param>
/// <param name="Role">Роль аккаунта</param>
/// <param name="AccessToken">JWT access-токен</param>
/// <param name="AccessTokenExpiresAtUtc">Момент истечения access-токена (UTC)</param>
/// <param name="RefreshToken">Refresh-токен (уходит в HttpOnly cookie)</param>
public record AuthTokens(
    Guid AccountId,
    Guid ClientId,
    AccountRole Role,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken);
