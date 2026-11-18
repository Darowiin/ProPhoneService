using System.Text.Json;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Обработка события сообщества, полученного через VK Long Poll
/// </summary>
public interface IVkUpdateHandler
{
    /// <summary>
    /// Обработать событие и завершить привязку получателя
    /// </summary>
    Task HandleAsync(JsonElement update, CancellationToken cancellationToken = default);
}
