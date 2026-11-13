using System.Net;
using System.Text.Json;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Infrastructure.EfCore.Notifications;

namespace ProPhoneService.Api.Tests;

public class TelegramPollingClientTests
{
    [Fact]
    public async Task DeleteWebhook_PreservesPendingUpdates()
    {
        string? body = null;
        string? url = null;
        using var http = Client(async (request, cancellation) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellation);
            url = request.RequestUri!.ToString();
            return Json("""{"ok":true,"result":true}""");
        });
        var client = new TelegramPollingClient(http, MessengerDeliveryTests.Options());

        await client.DeleteWebhookAsync();

        using var payload = JsonDocument.Parse(body!);
        Assert.False(payload.RootElement.GetProperty("drop_pending_updates").GetBoolean());
        Assert.EndsWith("/deleteWebhook", url);
    }

    [Fact]
    public async Task GetUpdates_SendsOffsetAndLongPollingParameters()
    {
        string? body = null;
        using var http = Client(async (request, cancellation) =>
        {
            body = await request.Content!.ReadAsStringAsync(cancellation);
            return Json("""{"ok":true,"result":[{"update_id":25,"message":{"text":"hello"}}]}""");
        });
        var client = new TelegramPollingClient(http, MessengerDeliveryTests.Options());

        var updates = await client.GetUpdatesAsync(25);

        using var payload = JsonDocument.Parse(body!);
        Assert.Equal(25, payload.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(25, payload.RootElement.GetProperty("timeout").GetInt32());
        Assert.Equal("message", payload.RootElement.GetProperty("allowed_updates")[0].GetString());
        Assert.Equal(25, Assert.Single(updates).GetProperty("update_id").GetInt64());
    }

    [Fact]
    public async Task RateLimit_UsesRetryAfterAndDoesNotExposeResponseSecrets()
    {
        using var http = Client((_, _) => Task.FromResult(Json(
            """{"ok":false,"error_code":429,"description":"secret","parameters":{"retry_after":30}}""",
            HttpStatusCode.TooManyRequests)));
        var client = new TelegramPollingClient(http, MessengerDeliveryTests.Options());

        var exception = await Assert.ThrowsAsync<TelegramPollingException>(() => client.GetUpdatesAsync(null));

        Assert.Equal(429, exception.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
        Assert.DoesNotContain("secret", exception.ToString());
    }

    [Fact]
    public async Task TransportError_DoesNotExposeBotToken()
    {
        var options = MessengerDeliveryTests.Options();
        using var http = Client((_, _) => throw new HttpRequestException("https://api.telegram.org/bot" + options.Telegram.Token));
        var client = new TelegramPollingClient(http, options);

        var exception = await Assert.ThrowsAsync<TelegramPollingException>(() => client.GetUpdatesAsync(null));

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(options.Telegram.Token, exception.ToString());
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var http = Client((_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        var client = new TelegramPollingClient(http, MessengerDeliveryTests.Options());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUpdatesAsync(null, cancellation.Token));
    }

    [Theory]
    [InlineData(TelegramUpdateMode.Webhook, false)]
    [InlineData(TelegramUpdateMode.Polling, true)]
    public void Polling_DoesNotRequireWebhookSecret(TelegramUpdateMode mode, bool expected)
    {
        var options = MessengerDeliveryTests.Options().Telegram;
        options.UpdateMode = mode;
        options.WebhookSecret = "";

        var configured = options.IsConfigured;

        Assert.Equal(expected, configured);
    }

    internal static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new Handler(send)) { BaseAddress = new Uri("https://api.telegram.org/") };

    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
