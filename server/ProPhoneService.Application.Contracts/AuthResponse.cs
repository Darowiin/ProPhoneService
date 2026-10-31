using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Ответ авторизации: идентификаторы и access-токен (refresh-токен - в HttpOnly cookie)
/// </summary>
/// <param name="AccountId">Идентификатор аккаунта</param>
/// <param name="ClientId">Идентификатор клиента</param>
/// <param name="Role">Роль аккаунта: Client или Master</param>
/// <param name="AccessToken">JWT access-токен</param>
/// <param name="AccessTokenExpiresAtUtc">Момент истечения access-токена (UTC)</param>
public record AuthResponse(
    Guid AccountId,
    Guid ClientId,
    AccountRole Role,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc);
