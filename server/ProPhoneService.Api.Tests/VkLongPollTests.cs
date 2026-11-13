using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Infrastructure.EfCore.Notifications;

namespace ProPhoneService.Api.Tests;

public class VkLongPollTests
{
    private const string Session = """{"response":{"server":"https://lp.vk.com/123","key":"session-secret","ts":"10"}}""";

    [Fact]
    public async Task Client_OpensSessionAndSendsLongPollParameters()
    {
        var requests = new List<string>();
        string? body = null;
        using var http = Http(async (request, ct) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(ct);
            }

            return Json(requests.Count == 1 ? Session : """{"ts":"11","updates":[]}""");
        });
        var client = new VkLongPollClient(http, MessengerDeliveryTests.Options());

        var session = await client.OpenAsync(CancellationToken.None);
        var result = await client.PollAsync(session, CancellationToken.None);

        Assert.EndsWith("groups.getLongPollServer", requests[0]);
        Assert.DoesNotContain("vk-secret", requests[0]);
        Assert.Contains("access_token=vk-secret", body);
        Assert.Contains("group_id=123", body);
        Assert.Contains("wait=25", requests[1]);
        Assert.Contains("ts=10", requests[1]);
        Assert.Equal("11", result.GetProperty("ts").GetString());
        Assert.Equal("10", session.Ts);
    }

    [Theory]
    [InlineData("http://lp.vk.com/123")]
    [InlineData("https://attacker.example/123")]
    [InlineData("https://lp.vk.com.attacker.example/123")]
    public async Task Client_RejectsUntrustedLongPollServer(string server)
    {
        using var http = Http((_, _) => Task.FromResult(Json(Session.Replace("https://lp.vk.com/123", server))));
        var client = new VkLongPollClient(http, MessengerDeliveryTests.Options());

        await Assert.ThrowsAsync<VkLongPollException>(() => client.OpenAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"error\":{\"error_code\":5,\"error_msg\":\"vk-secret\"}}")]
    public async Task Client_InvalidResponsesDoNotExposeSecrets(string response)
    {
        using var http = Http((_, _) => Task.FromResult(Json(response)));
        var client = new VkLongPollClient(http, MessengerDeliveryTests.Options());

        var exception = await Assert.ThrowsAsync<VkLongPollException>(() => client.OpenAsync(CancellationToken.None));

        Assert.DoesNotContain("vk-secret", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Client_TransportFailureIsSanitizedAndCancellationPropagates()
    {
        using var http = Http((_, ct) => ct.IsCancellationRequested
            ? Task.FromCanceled<HttpResponseMessage>(ct)
            : throw new HttpRequestException("session-secret"));
        var client = new VkLongPollClient(http, MessengerDeliveryTests.Options());
        using var cancellation = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<VkLongPollException>(() => client.OpenAsync(CancellationToken.None));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.OpenAsync(cancellation.Token));

        Assert.DoesNotContain("session-secret", exception.ToString());
    }

    [Theory]
    [InlineData(1, "99", 1)]
    [InlineData(2, "10", 2)]
    [InlineData(3, "20", 2)]
    public async Task Worker_RecoversFromFailedResponses(int failed, string expectedTs, int expectedOpens)
    {
        var opens = 0;
        var polls = 0;
        string? lastQuery = null;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Http(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("groups.getLongPollServer"))
            {
                opens++;
                return Json(opens == 1 ? Session : Session.Replace("\"10\"", "\"20\""));
            }

            polls++;
            if (polls == 1)
            {
                return Json($"{{\"failed\":{failed},\"ts\":\"99\"}}");
            }

            lastQuery = request.RequestUri.Query;
            done.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        });
        using var provider = Services(Mock.Of<IVkUpdateHandler>());
        using var worker = Worker(provider, http);

        await RunUntil(worker, done.Task);

        Assert.Equal(expectedOpens, opens);
        Assert.Contains("ts=" + expectedTs, lastQuery);
    }

    [Fact]
    public async Task Worker_RetriesFailedEventWithoutRepeatingSuccessfulEventsOrAdvancingTs()
    {
        var handler = new Mock<IVkUpdateHandler>();
        var seen = new List<int>();
        var failures = 0;
        handler.Setup(x => x.HandleAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .Returns((JsonElement value, CancellationToken _) =>
            {
                var id = value.GetProperty("id").GetInt32();
                seen.Add(id);
                if (id == 2 && failures++ == 0) throw new InvalidOperationException("secret");
                return Task.CompletedTask;
            });
        var polls = 0;
        string? query = null;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Http(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("groups.getLongPollServer")) return Json(Session);
            polls++;
            if (polls == 1) return Json("""{"ts":"11","updates":[{"id":1},{"id":2}]}""");
            query = request.RequestUri.Query;
            done.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        });
        using var provider = Services(handler.Object);
        using var worker = Worker(provider, http);

        await RunUntil(worker, done.Task);

        Assert.Equal(new[] { 1, 2, 2 }, seen);
        Assert.Equal(2, polls);
        Assert.Contains("ts=11", query);
    }

    [Fact]
    public async Task Worker_WithoutSettingsDoesNotCallVk()
    {
        var calls = 0;
        using var http = Http((_, _) => { calls++; return Task.FromResult(Json("{}")); });
        using var provider = Services(Mock.Of<IVkUpdateHandler>());
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("VkLongPoll")).Returns(http);
        using var worker = new VkLongPollService(provider.GetRequiredService<IServiceScopeFactory>(), factory.Object,
            new(), NullLogger<VkLongPollService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, calls);
    }

    private static async Task RunUntil(VkLongPollService worker, Task done)
    {
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await done.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider Services(IVkUpdateHandler handler)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => handler);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static VkLongPollService Worker(ServiceProvider provider, HttpClient http)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("VkLongPoll")).Returns(http);
        return new(provider.GetRequiredService<IServiceScopeFactory>(), factory.Object,
            MessengerDeliveryTests.Options(), NullLogger<VkLongPollService>.Instance);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new Handler(send)) { BaseAddress = new Uri("https://api.vk.com/method/") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
