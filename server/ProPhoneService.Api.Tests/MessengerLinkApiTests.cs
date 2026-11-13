using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Infrastructure.EfCore.Notifications;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text;

namespace ProPhoneService.Api.Tests;

public class MessengerLinkApiTests
{
    private static WebApplicationFactory<Program> Factory(Mock<IMessengerBindingRepository> repository) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Auth:MasterEmail", "");
            host.UseSetting("Database:MigrateOnStartup", "false");
            host.ConfigureTestServices(services =>
            {
                var notifier = new Mock<INotifier>();
                notifier.Setup(x => x.SendAsync(It.IsAny<MessengerChannel>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

                var polling = services.Single(x => x.ImplementationType == typeof(VkLongPollService));
                services.Remove(polling);
                services.RemoveAll<INotifier>();
                services.AddSingleton(notifier.Object);
                services.RemoveAll<MessengerOptions>();
                services.AddSingleton(MessengerDeliveryTests.Options());
                services.RemoveAll<IMessengerBindingRepository>();
                services.AddScoped(_ => repository.Object);
            });
        });

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

        var response = await client.PostAsJsonAsync($"/api/messengers/orders/{order.Id}/link", new { channel = "Vk" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("offlineOrderAvailable").GetBoolean());
        Assert.Contains("мастерской", body.RootElement.GetProperty("detail").GetString());
        repository.Verify(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"channel\":\"Vk\"}")]
    public async Task OrderLink_DefaultsToVk(string json)
    {
        var repository = new Mock<IMessengerBindingRepository>();
        var orders = new Mock<IRepairOrderRepository>();
        var order = RepairOrder.CreatePending(Guid.NewGuid(), "legacy@example.com", "+79001234567");
        order.CreatedAt = DateTime.UtcNow;
        orders.Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        using var factory = Factory(repository).WithWebHostBuilder(host => host.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRepairOrderRepository>();
            services.AddScoped(_ => orders.Object);
        }));
        using var client = factory.CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/messengers/orders/{order.Id}/link", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Vk", body.RootElement.GetProperty("channel").GetString());
        Assert.StartsWith("https://vk.me/club123?ref=", body.RootElement.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("{\"channel\":\"Other\"}")]
    [InlineData("{\"channel\":0}")]
    [InlineData("{\"channel\":null}")]
    public async Task UnsupportedChannel_IsRejected(string json)
    {
        var repository = new Mock<IMessengerBindingRepository>();
        using var factory = Factory(repository);
        using var client = factory.CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/messengers/orders/{Guid.NewGuid()}/link", content);

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
}
