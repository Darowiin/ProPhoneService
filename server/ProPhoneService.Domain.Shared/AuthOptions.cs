namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Настройки входа по коду и выдачи токенов
/// </summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Срок жизни одноразового кода, мин
    /// </summary>
    public int CodeLifetimeMinutes { get; set; } = 5;

    /// <summary>
    /// Лимит неверных кодов до блокировки контакта
    /// </summary>
    public int MaxFailedCodeAttempts { get; set; } = 5;

    /// <summary>
    /// Время блокировки контакта после превышения лимита, мин
    /// </summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Срок жизни access-токена JWT, мин
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    /// <summary>
    /// Срок жизни refresh-токена, дней
    /// </summary>
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    /// <summary>
    /// Минимальный интервал между запросами кода, сек
    /// </summary>
    public int RequestCodeCooldownSeconds { get; set; } = 60;
}
