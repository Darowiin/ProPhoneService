using System.Net;
using System.Text.Json;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Infrastructure.EfCore.Notifications;

namespace ProPhoneService.Api.Tests;

public class MessengerDeliveryTests
{
    internal static MessengerOptions Options() => new()
    {
        Telegram = new() { Token = "123:telegram-secret", BotUsername = "test_bot", WebhookSecret = new string('t', 32) },
        Vk = new() { Token = "vk-secret", GroupId = 123, WebhookSecret = new string('v', 32), ConfirmationCode = "confirm" },
    };

    [Theory]
    [InlineData(MessengerChannel.Telegram)]
    [InlineData(MessengerChannel.Vk)]
    public async Task Router_SendsOnlyThroughSelectedChannel(MessengerChannel channel)
    {
        var options = Options();
        var telegramHandler = new Handler("""{"ok":true}""");
        var vkHandler = new Handler("""{"response":17}""");
        using var telegramClient = new HttpClient(telegramHandler) { BaseAddress = new Uri("https://api.telegram.org/") };
        using var vkClient = new HttpClient(vkHandler) { BaseAddress = new Uri("https://api.vk.com/method/") };
        var router = new MessengerNotifier(new(telegramClient, options), new(vkClient, options), Mock.Of<IMessengerBindingRepository>());

        await router.SendAsync(channel, "12345", "Код 123456");

        Assert.Equal(channel == MessengerChannel.Telegram ? 1 : 0, telegramHandler.Calls);
        Assert.Equal(channel == MessengerChannel.Vk ? 1 : 0, vkHandler.Calls);
        if (channel == MessengerChannel.Telegram)
        {
            using var body = JsonDocument.Parse(telegramHandler.Body!);
            Assert.Equal(12345, body.RootElement.GetProperty("chat_id").GetInt64());
            Assert.Equal("Код 123456", body.RootElement.GetProperty("text").GetString());
            Assert.EndsWith("/sendMessage", telegramHandler.Url);
        }
        else
        {
            Assert.EndsWith("/messages.send", vkHandler.Url);
            Assert.Contains("user_id=12345", vkHandler.Body);
            Assert.Contains("random_id=", vkHandler.Body);
            Assert.DoesNotContain(options.Vk.Token, vkHandler.Url!);
        }
    }

    [Theory]
    [InlineData(MessengerChannel.Telegram, """{"ok":false,"description":"blocked"}""", 200)]
    [InlineData(MessengerChannel.Vk, """{"error":{"error_code":901}}""", 200)]
    [InlineData(MessengerChannel.Telegram, "{}", 503)]
    [InlineData(MessengerChannel.Vk, "{}", 429)]
    [InlineData(MessengerChannel.Telegram, "invalid json", 200)]
    public async Task ProviderError_ReturnsSafeUnavailableException(MessengerChannel channel, string json, int status)
    {
        var options = Options();
        var handler = new Handler(json, (HttpStatusCode)status);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var router = new MessengerNotifier(new(client, options), new(client, options), Mock.Of<IMessengerBindingRepository>());

        var exception = await Assert.ThrowsAsync<MessengerUnavailableException>(() => router.SendAsync(channel, "42", "code"));

        Assert.Equal(channel, exception.Channel);
        Assert.Contains("мастерской", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(options.Telegram.Token, exception.ToString());
        Assert.DoesNotContain(options.Vk.Token, exception.ToString());
    }

    [Theory]
    [InlineData(MessengerChannel.Telegram)]
    [InlineData(MessengerChannel.Vk)]
    public async Task MissingConfiguration_DoesNotSendAndDoesNotFallback(MessengerChannel channel)
    {
        var handler = new Handler("{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var options = new MessengerOptions();
        var router = new MessengerNotifier(new(client, options), new(client, options), Mock.Of<IMessengerBindingRepository>());

        await Assert.ThrowsAsync<MessengerUnavailableException>(() => router.SendAsync(channel, "42", "code"));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        var handler = new Handler("{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var notifier = new TelegramBotNotifier(client, Options());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => notifier.SendAsync("42", "code", cancellation.Token));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task LegacyEmailTarget_IsRejectedWithoutSending()
    {
        var handler = new Handler("{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var repository = new Mock<IMessengerBindingRepository>();
        var router = new MessengerNotifier(new(client, Options()), new(client, Options()), repository.Object);

        await Assert.ThrowsAsync<MessengerUnavailableException>(() => router.SendAsync("email@example.com", "subject", "code"));

        Assert.Equal(0, handler.Calls);
        repository.Verify(x => x.GetByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    internal sealed class Handler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public string? Url { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
    }
}
