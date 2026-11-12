using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Отправка сообщений через Telegram Bot API
/// </summary>
public class TelegramBotNotifier(HttpClient httpClient, MessengerOptions options)
{
    /// <summary>
    /// Отправить сообщение в личный чат с ботом
    /// </summary>
    public async Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
    {
        if (!options.Telegram.IsConfigured)
            throw new MessengerUnavailableException(MessengerChannel.Telegram);

        if (!long.TryParse(recipientId, NumberStyles.None, CultureInfo.InvariantCulture, out var chatId) || chatId <= 0)
            throw new ArgumentException("Нужен идентификатор личного чата Telegram", nameof(recipientId));

        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"bot{options.Telegram.Token}/sendMessage", new { chat_id = chatId, text }, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new MessengerUnavailableException(MessengerChannel.Telegram);

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                throw new MessengerUnavailableException(MessengerChannel.Telegram);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Исключение транспорта может содержать URL с токеном бота, наружу его не передаём
            throw new MessengerUnavailableException(MessengerChannel.Telegram);
        }
    }
}
