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
        Vk = new() { Token = "vk-secret", GroupId = 123 },
    };

    [Fact]
    public async Task Router_SendsThroughVk()
    {
        var options = Options();
        var handler = new Handler("""{"response":17}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.vk.com/method/") };
        var router = new MessengerNotifier(new(client, options), Mock.Of<IMessengerBindingRepository>());

        await router.SendAsync(MessengerChannel.Vk, "12345", "Код 123456");

        Assert.Equal(1, handler.Calls);
        Assert.EndsWith("/messages.send", handler.Url);
        Assert.Contains("user_id=12345", handler.Body);
        Assert.Contains("random_id=", handler.Body);
        Assert.DoesNotContain(options.Vk.Token, handler.Url!);
    }

    [Theory]
    [InlineData(MessengerChannel.Vk, """{"error":{"error_code":901}}""", 200)]
    [InlineData(MessengerChannel.Vk, "{}", 429)]
    [InlineData(MessengerChannel.Vk, "invalid json", 200)]
    public async Task ProviderError_ReturnsSafeUnavailableException(MessengerChannel channel, string json, int status)
    {
        var options = Options();
        var handler = new Handler(json, (HttpStatusCode)status);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var router = new MessengerNotifier(new(client, options), Mock.Of<IMessengerBindingRepository>());

        var exception = await Assert.ThrowsAsync<MessengerUnavailableException>(() => router.SendAsync(channel, "42", "code"));

        Assert.Equal(channel, exception.Channel);
        Assert.Contains("мастерской", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(options.Vk.Token, exception.ToString());
    }

    [Theory]
    [InlineData(MessengerChannel.Vk)]
    public async Task MissingConfiguration_DoesNotSendAndDoesNotFallback(MessengerChannel channel)
    {
        var handler = new Handler("{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var options = new MessengerOptions();
        var router = new MessengerNotifier(new(client, options), Mock.Of<IMessengerBindingRepository>());

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
        var notifier = new VkNotifier(client, Options());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => notifier.SendAsync("42", "code", cancellation.Token));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task LegacyEmailTarget_IsRejectedWithoutSending()
    {
        var handler = new Handler("{}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var repository = new Mock<IMessengerBindingRepository>();
        var router = new MessengerNotifier(new(client, Options()), repository.Object);

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
