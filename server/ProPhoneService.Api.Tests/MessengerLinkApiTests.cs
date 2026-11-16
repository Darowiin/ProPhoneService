using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Infrastructure.EfCore.Notifications;

namespace ProPhoneService.Api.Tests;

public class MessengerLinkApiTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    private static WebApplicationFactory<Program> Factory(Mock<IMessengerBindingRepository> repository) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.UseSetting("ConnectionStrings:Default", "");
            host.UseSetting("Auth:MasterPhone", "");
            host.UseSetting("Auth:MasterVkUserId", "0");
            host.UseSetting("Jwt:Key", TestJwt.Key);
            host.UseSetting("ReverseProxy:KnownProxies", "10.20.30.40");
            host.ConfigureTestServices(services =>
            {
                var polling = services.Single(x => x.ImplementationType == typeof(VkLongPollService));
                services.Remove(polling);

                var expiration = services.Single(x => x.ImplementationType == typeof(PendingOrderExpirationService));
                services.Remove(expiration);

                var accounts = new Mock<IAccountRepository>();
                accounts.Setup(x => x.GetByIdAsync(AccountId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Account { Id = AccountId, ClientId = ClientId });
                var clients = new Mock<IClientRepository>();
                clients.Setup(x => x.GetByIdAsync(ClientId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Client { Id = ClientId, Name = "Клиент", Phone = "+79001234567" });

                services.RemoveAll<IAccountRepository>();
                services.AddSingleton(accounts.Object);
                services.RemoveAll<IClientRepository>();
                services.AddSingleton(clients.Object);
                services.RemoveAll<MessengerOptions>();
                services.AddSingleton(MessengerDeliveryTests.Options());
                services.RemoveAll<IMessengerBindingRepository>();
                services.AddSingleton(repository.Object);
            });
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuestOrderLinkEndpoint_IsNotAvailable(bool authenticated)
    {
        var repository = new Mock<IMessengerBindingRepository>(MockBehavior.Strict);
        var orders = new Mock<IRepairOrderRepository>(MockBehavior.Strict);
        var order = RepairOrder.CreatePending(Guid.NewGuid(), "+79001234567");
        using var factory = Factory(repository).WithWebHostBuilder(host => host.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRepairOrderRepository>();
            services.AddSingleton(orders.Object);
        }));
        using var client = factory.CreateClient();
        if (authenticated)
        {
            client.WithToken(TestJwt.ForClient(ClientId, AccountId));
        }

        var response = await client.PostAsJsonAsync($"/api/messengers/orders/{order.Id}/link", new { channel = "Vk" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        orders.VerifyNoOtherCalls();
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnconfiguredMessenger_Returns503WithOfflineOrderHint()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository).WithWebHostBuilder(host => host.ConfigureTestServices(services =>
        {
            services.RemoveAll<MessengerOptions>();
            services.AddSingleton(new MessengerOptions());
        }));
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(ClientId, AccountId));

        var response = await client.PostAsJsonAsync("/api/messengers/me/link", new { channel = "Vk" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("offlineOrderAvailable").GetBoolean());
        repository.Verify(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"channel\":\"Vk\"}")]
    public async Task AccountLink_DefaultsToVkAndUsesAuthenticatedAccount(string json)
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(ClientId, AccountId));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/messengers/me/link", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Vk", body.RootElement.GetProperty("channel").GetString());
        Assert.StartsWith("https://vk.me/club123?ref=", body.RootElement.GetProperty("url").GetString());
        repository.Verify(x => x.AddAsync(It.Is<MessengerBinding>(binding =>
            binding.AccountId == AccountId && binding.OrderId == null && binding.Phone == "+79001234567"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("{\"channel\":\"Other\"}")]
    [InlineData("{\"channel\":0}")]
    [InlineData("{\"channel\":null}")]
    public async Task UnsupportedChannel_IsRejected(string json)
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(ClientId, AccountId));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/messengers/me/link", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        repository.Verify(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AccountLink_RequiresAuthentication()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messengers/me/link", new { channel = "Vk" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        repository.Verify(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("10.20.30.40")]
    [InlineData("::ffff:10.20.30.40")]
    public async Task TrustedProxy_RateLimitsEachClientSeparately(string proxy)
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        var responses = new List<int>();

        for (var index = 0; index < 11; index++)
        {
            var response = await SendFrom(factory, proxy, "198.51.100.1");
            responses.Add(response.Response.StatusCode);
        }
        var mappedSameClient = await SendFrom(factory, proxy, "::ffff:198.51.100.1");
        var otherClient = await SendFrom(factory, proxy, "198.51.100.2");

        Assert.All(responses.Take(10), status => Assert.Equal(StatusCodes.Status200OK, status));
        Assert.Equal(StatusCodes.Status429TooManyRequests, responses.Last());
        Assert.Equal(StatusCodes.Status429TooManyRequests, mappedSameClient.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, otherClient.Response.StatusCode);
        Assert.Equal(IPAddress.Parse("198.51.100.2"), otherClient.Connection.RemoteIpAddress);
        Assert.Equal("https", otherClient.Request.Scheme);
    }

    [Fact]
    public async Task UntrustedPeer_CannotBypassLimitWithSpoofedHeaders()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        var responses = new List<HttpContext>();

        for (var index = 0; index < 11; index++)
        {
            responses.Add(await SendFrom(factory, "10.20.30.41", $"198.51.100.{index + 1}"));
        }

        Assert.All(responses.Take(10), response => Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode));
        Assert.Equal(StatusCodes.Status429TooManyRequests, responses.Last().Response.StatusCode);
        Assert.All(responses, response =>
        {
            Assert.Equal(IPAddress.Parse("10.20.30.41"), response.Connection.RemoteIpAddress);
            Assert.Equal("http", response.Request.Scheme);
        });
    }

    [Fact]
    public async Task TrustedProxy_UsesOnlyTheLastForwardedAddress()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        var responses = new List<HttpContext>();

        for (var index = 0; index < 11; index++)
        {
            responses.Add(await SendFrom(factory, "10.20.30.40", $"203.0.113.{index + 1}, 198.51.100.1"));
        }

        Assert.All(responses.Take(10), response => Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode));
        Assert.Equal(StatusCodes.Status429TooManyRequests, responses.Last().Response.StatusCode);
        Assert.All(responses, response => Assert.Equal(IPAddress.Parse("198.51.100.1"), response.Connection.RemoteIpAddress));
    }

    private static Task<HttpContext> SendFrom(WebApplicationFactory<Program> factory, string peer, string forwardedFor) =>
        factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = "POST";
            context.Request.Path = "/api/messengers/me/link";
            context.Request.Scheme = "http";
            context.Request.Headers.Authorization = "Bearer " + TestJwt.ForClient(ClientId, AccountId);
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = 2;
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        });
}
