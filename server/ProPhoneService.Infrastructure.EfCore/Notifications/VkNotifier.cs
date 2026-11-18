using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Отправка сообщений от имени сообщества VK
/// </summary>
public class VkNotifier(HttpClient httpClient, MessengerOptions options)
{
    /// <summary>
    /// Отправить сообщение пользователю, разрешившему сообщения сообщества
    /// </summary>
    public async Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
    {
        if (!options.Vk.IsConfigured)
            throw new MessengerUnavailableException(MessengerChannel.Vk);

        if (!long.TryParse(recipientId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            throw new ArgumentException("Нужен идентификатор пользователя VK", nameof(recipientId));

        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["access_token"] = options.Vk.Token,
                ["v"] = options.Vk.ApiVersion,
                ["user_id"] = recipientId,
                ["random_id"] = RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString(CultureInfo.InvariantCulture),
                ["message"] = text,
            });

            using var response = await httpClient.PostAsync("messages.send", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new MessengerUnavailableException(MessengerChannel.Vk);

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (json.RootElement.ValueKind != JsonValueKind.Object || json.RootElement.TryGetProperty("error", out _)
                || !json.RootElement.TryGetProperty("response", out var result)
                || result.ValueKind != JsonValueKind.Number || !result.TryGetInt64(out var messageId) || messageId <= 0)
                throw new MessengerUnavailableException(MessengerChannel.Vk);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new MessengerUnavailableException(MessengerChannel.Vk);
        }
    }
}
