namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Имена клеймов JWT-токена: единый источник для выдачи токена и проверки доступа
/// </summary>
public static class AuthClaims
{
    /// <summary>
    /// Идентификатор аккаунта
    /// </summary>
    public const string Subject = "sub";

    /// <summary>
    /// Идентификатор клиента (по нему фильтруются клиентские запросы)
    /// </summary>
    public const string ClientId = "client_id";

    /// <summary>
    /// Роль аккаунта (<see cref="Enum.AccountRole"/>)
    /// </summary>
    public const string Role = "role";
}
