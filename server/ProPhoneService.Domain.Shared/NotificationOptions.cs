namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Настройки отправки уведомлений: повторы при сбое провайдера
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>
    /// Всего попыток отправки, включая первую
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Базовая пауза перед повтором; растёт вдвое с каждой следующей попыткой
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
