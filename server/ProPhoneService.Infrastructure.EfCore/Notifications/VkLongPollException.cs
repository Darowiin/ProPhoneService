namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Ошибка VK без секретов запроса и содержимого ответа
/// </summary>
public class VkLongPollException(int code) : Exception($"VK Long Poll: ошибка {code}")
{
    /// <summary>
    /// Код VK API или HTTP, ноль при ошибке транспорта или формата
    /// </summary>
    public int Code { get; } = code;
}
