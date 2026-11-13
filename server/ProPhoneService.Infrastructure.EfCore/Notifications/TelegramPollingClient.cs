using System.Net.Http.Json;
using System.Text.Json;
using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Получение Telegram-событий исходящими HTTPS-запросами
/// </summary>
public class TelegramPollingClient(HttpClient httpClient, MessengerOptions options)
{
    /// <summary>
    /// Отключить webhook, сохранив ожидающие события
    /// </summary>
    public async Task DeleteWebhookAsync(CancellationToken cancellationToken = default)
    {
        using var response = await CallAsync("deleteWebhook", new { drop_pending_updates = false }, cancellationToken);

        if (!response.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.True)
        {
            throw new TelegramPollingException(0);
        }
    }

    /// <summary>
    /// Получить до ста событий с ожиданием до 25 секунд
    /// </summary>
    /// <param name="offset">Идентификатор первого ещё не обработанного события</param>
    /// <param name="cancellationToken">Отмена запроса</param>
    public async Task<JsonElement[]> GetUpdatesAsync(long? offset, CancellationToken cancellationToken = default)
    {
        using var response = await CallAsync("getUpdates", new
        {
            offset = offset ?? 0,
            timeout = 25,
            limit = 100,
            allowed_updates = new[] { "message" },
        }, cancellationToken);

        if (!response.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            throw new TelegramPollingException(0);
        }

        var updates = result.EnumerateArray().Select(update => update.Clone()).ToArray();

        foreach (var update in updates)
        {
            if (update.ValueKind != JsonValueKind.Object
                || !update.TryGetProperty("update_id", out var id) || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt64(out var number) || number < 0 || number == long.MaxValue)
            {
                throw new TelegramPollingException(0);
            }
        }

        return updates;
    }

    private async Task<JsonDocument> CallAsync(string method, object body, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"bot{options.Telegram.Token}/{method}", body, cancellationToken);

            using var json = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new TelegramPollingException((int)response.StatusCode);
            }

            var root = json.RootElement;

            if (!response.IsSuccessStatusCode || !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            {
                var code = root.TryGetProperty("error_code", out var errorCode)
                    && errorCode.ValueKind == JsonValueKind.Number && errorCode.TryGetInt32(out var number)
                    ? number : (int)response.StatusCode;

                var retryAfter = 5;

                if (root.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("retry_after", out var retry) && retry.ValueKind == JsonValueKind.Number
                    && retry.TryGetInt32(out var seconds))
                {
                    retryAfter = seconds;
                }

                throw new TelegramPollingException(code, retryAfter);
            }

            return JsonDocument.Parse(root.GetRawText());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Не сохраняем исходное исключение: URL запроса содержит токен бота
            throw new TelegramPollingException(0);
        }
    }
}
