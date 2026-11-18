namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Параметры сессии Long Poll, содержащие секретный ключ
/// </summary>
public class VkLongPollSession
{
    /// <summary>
    /// Адрес сервера событий VK
    /// </summary>
    public required Uri Server { get; init; }

    /// <summary>
    /// Секрет сессии
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Позиция в потоке событий
    /// </summary>
    public required string Ts { get; set; }
}
