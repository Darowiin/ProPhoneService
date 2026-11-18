using System.Globalization;
using System.Text.Json;
using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Безопасный клиент VK API и Bots Long Poll
/// </summary>
public class VkLongPollClient(HttpClient httpClient, MessengerOptions options)
{
    /// <summary>
    /// Получить адрес, ключ и начальную позицию Long Poll
    /// </summary>
    public async Task<VkLongPollSession> OpenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "groups.getLongPollServer")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["access_token"] = options.Vk.Token,
                ["group_id"] = options.Vk.GroupId.ToString(CultureInfo.InvariantCulture),
                ["v"] = options.Vk.ApiVersion,
            }),
        };
        var root = await SendAsync(request, cancellationToken);

        if (root.TryGetProperty("error", out var error))
        {
            var code = error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("error_code", out var number) && number.ValueKind == JsonValueKind.Number
                && number.TryGetInt32(out var result)
                ? result : 0;

            throw new VkLongPollException(code);
        }

        if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object
            || !Uri.TryCreate(String(response, "server"), UriKind.Absolute, out var server)
            || server.Scheme != "https" || !server.IsDefaultPort || server.UserInfo.Length != 0
            || !(server.Host.EndsWith(".vk.com", StringComparison.OrdinalIgnoreCase)
                || server.Host.EndsWith(".vk.ru", StringComparison.OrdinalIgnoreCase))
            || String(response, "key") is not { Length: > 0 } key
            || String(response, "ts") is not { Length: > 0 } ts)
        {
            throw new VkLongPollException(0);
        }

        return new VkLongPollSession { Server = server, Key = key, Ts = ts };
    }

    /// <summary>
    /// Ожидать события до 25 секунд, не изменяя позицию сессии
    /// </summary>
    public async Task<JsonElement> PollAsync(VkLongPollSession session, CancellationToken cancellationToken)
    {
        var separator = string.IsNullOrEmpty(session.Server.Query) ? "?" : "&";
        var url = session.Server.AbsoluteUri + separator + "act=a_check&wait=25&key="
            + Uri.EscapeDataString(session.Key) + "&ts=" + Uri.EscapeDataString(session.Ts);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        return await SendAsync(request, cancellationToken);
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new VkLongPollException((int)response.StatusCode);
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new VkLongPollException(0);
            }

            return json.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Исходное исключение может содержать URL с ключом сессии
            throw new VkLongPollException(0);
        }
    }

    internal static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
