using System.ComponentModel.DataAnnotations;

namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Настройки подписи JWT-токенов
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Симметричный ключ подписи; секрет - в окружении, вне репозитория
    /// </summary>
    [Required(ErrorMessage = "Ключ подписи Jwt:Key не задан")]
    public string? Key { get; set; }

    /// <summary>
    /// Издатель токена (issuer)
    /// </summary>
    public string Issuer { get; set; } = "ProPhoneService";

    /// <summary>
    /// Аудитория токена (audience)
    /// </summary>
    public string Audience { get; set; } = "ProPhoneService";
}
