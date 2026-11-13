using System.Text.Json;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Обработка Telegram-событий независимо от транспорта получения
/// </summary>
public interface ITelegramUpdateHandler
{
    /// <summary>
    /// Обработать входящее событие
    /// </summary>
    /// <param name="update">Событие Telegram</param>
    /// <param name="cancellationToken">Отмена обработки</param>
    Task HandleAsync(JsonElement update, CancellationToken cancellationToken = default);
}
