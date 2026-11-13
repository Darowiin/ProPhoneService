namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Ошибка Telegram polling без URL, токенов и содержимого событий
/// </summary>
/// <param name="errorCode">HTTP-код или код Telegram API</param>
/// <param name="retryAfterSeconds">Пауза перед повтором</param>
public class TelegramPollingException(int errorCode, int retryAfterSeconds = 5)
    : Exception($"Telegram polling недоступен, код {errorCode}")
{
    /// <summary>
    /// Код ошибки провайдера
    /// </summary>
    public int ErrorCode { get; } = errorCode;

    /// <summary>
    /// Пауза перед повтором с учётом ограничения провайдера
    /// </summary>
    public TimeSpan RetryAfter { get; } = TimeSpan.FromSeconds(Math.Clamp(retryAfterSeconds, 5, 86400));
}
