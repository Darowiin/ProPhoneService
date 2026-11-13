namespace ProPhoneService.Domain.Shared.Enum;

/// <summary>
/// Способ получения входящих событий Telegram
/// </summary>
public enum TelegramUpdateMode
{
    /// <summary>
    /// Приём событий через публичный HTTPS endpoint
    /// </summary>
    Webhook,

    /// <summary>
    /// Получение событий исходящими запросами getUpdates
    /// </summary>
    Polling,
}
