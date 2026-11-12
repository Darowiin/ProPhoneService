using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class MessengerWebhookTests
{
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static WebApplicationFactory<Program> Factory(Mock<IMessengerBindingRepository> repository) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Auth:MasterEmail", "");
            host.UseSetting("Database:MigrateOnStartup", "false");
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<MessengerOptions>();
                services.AddSingleton(MessengerDeliveryTests.Options());
                services.RemoveAll<IMessengerBindingRepository>();
                services.AddScoped(_ => repository.Object);
            });
        });

    [Theory]
    [InlineData(false, "private", 42, 42)]
    [InlineData(true, "group", 42, 42)]
    [InlineData(true, "private", 43, 42)]
    public async Task Telegram_RejectsUntrustedCallbacks(bool validSecret, string chatType, long chatId, long fromId)
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", validSecret ? new string('t', 32) : "invalid");

        var response = await client.PostAsJsonAsync("/api/messengers/telegram/webhook", new
        {
            message = new { text = "/start " + Token, chat = new { id = chatId, type = chatType }, from = new { id = fromId } },
        });

        Assert.Equal(validSecret ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        repository.Verify(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Telegram_PrivateStart_BindsSenderFromVerifiedCallback()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        repository.Setup(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessengerBinding { Id = Guid.NewGuid(), TokenHash = "hash", Phone = "+79001234567",
                Channel = MessengerChannel.Telegram, AccountId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        repository.Setup(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Telegram, "42",
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var factory = Factory(repository);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", new string('t', 32));

        var response = await client.PostAsJsonAsync("/api/messengers/telegram/webhook", new
        {
            message = new { text = "/start " + Token, chat = new { id = 42, type = "private" }, from = new { id = 42 } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        repository.Verify(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Telegram, "42",
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(123, true, HttpStatusCode.OK)]
    [InlineData(124, true, HttpStatusCode.Unauthorized)]
    [InlineData(123, false, HttpStatusCode.Unauthorized)]
    public async Task Vk_Confirmation_RequiresSecretAndMatchingCommunity(long groupId, bool validSecret, HttpStatusCode expected)
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messengers/vk/webhook", new
        {
            type = "confirmation", group_id = groupId, secret = validSecret ? new string('v', 32) : "invalid",
        });

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK) Assert.Equal("confirm", await response.Content.ReadAsStringAsync());
        repository.Verify(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Vk_PrivateMessage_BindsRefToVerifiedSender()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        repository.Setup(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessengerBinding { Id = Guid.NewGuid(), TokenHash = "hash", Phone = "+79001234567",
                Channel = MessengerChannel.Vk, AccountId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        repository.Setup(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Vk, "42",
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var factory = Factory(repository);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messengers/vk/webhook", new
        {
            type = "message_new", group_id = 123, secret = new string('v', 32),
            @object = new { message = new { from_id = 42, peer_id = 42, @ref = Token } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        repository.Verify(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Vk, "42",
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnconfiguredMessenger_Returns503WithOfflineOrderHint()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        var orders = new Mock<IRepairOrderRepository>();
        var order = RepairOrder.CreatePending(Guid.NewGuid(), "legacy@example.com", "+79001234567");
        order.CreatedAt = DateTime.UtcNow;
        orders.Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        using var factory = Factory(repository).WithWebHostBuilder(host => host.ConfigureTestServices(services =>
        {
            services.RemoveAll<MessengerOptions>();
            services.AddSingleton(new MessengerOptions());
            services.RemoveAll<IRepairOrderRepository>();
            services.AddScoped(_ => orders.Object);
        }));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/messengers/orders/{order.Id}/link", new { channel = "Telegram" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("offlineOrderAvailable").GetBoolean());
        Assert.Contains("мастерской", body.RootElement.GetProperty("detail").GetString());
        repository.Verify(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AccountLink_RequiresAuthentication()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/messengers/me/link", new { channel = "Telegram" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        repository.Verify(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
