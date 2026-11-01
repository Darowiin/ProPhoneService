using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

/// <summary>
/// GET /api/master/stats - только мастер, период и агрегаты в ответе
/// </summary>
public class MasterStatsEndpointTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private class StatsSource
    {
        public Dictionary<DeviceType, int> Counts { get; init; } = [];

        public decimal Total { get; init; }

        public Mock<IRepairOrderRepository> Repository { get; } = new();

        public WebApplicationFactory<Program> CreateFactory()
        {
            Repository
                .Setup(r => r.CountCompletedByDeviceTypeAsync(
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Counts);
            Repository
                .Setup(r => r.SumCompletedOrdersAsync(
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Total);

            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("Jwt:Key", TestJwt.Key);
                host.UseSetting("Database:MigrateOnStartup", "false");
                host.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IRepairOrderRepository>();
                    services.AddScoped(_ => Repository.Object);
                });
            });
        }
    }

    [Fact]
    public async Task GetStats_Anonymous_Returns401()
    {
        using var factory = new StatsSource().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/master/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_ClientToken_Returns403()
    {
        using var factory = new StatsSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(Guid.NewGuid()));

        var response = await client.GetAsync("/api/master/stats");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStats_MasterToken_ReturnsAggregates()
    {
        var source = new StatsSource
        {
            Counts = new Dictionary<DeviceType, int> { [DeviceType.Phone] = 4, [DeviceType.Tablet] = 1 },
            Total = 21500m,
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/master/stats?period=All");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = await response.Content.ReadFromJsonAsync<StatsDto>(_jsonOptions);
        Assert.NotNull(stats);
        Assert.Null(stats.FromUtc);
        Assert.Equal(21500m, stats.CompletedTotalPrice);
        Assert.Collection(
            stats.CompletedByDeviceType,
            x => Assert.Equal((DeviceType.Phone, 4), (x.Type, x.Count)),
            x => Assert.Equal((DeviceType.Tablet, 1), (x.Type, x.Count)));
    }

    [Fact]
    public async Task GetStats_DefaultPeriod_IsCurrentMonth()
    {
        using var factory = new StatsSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/master/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = await response.Content.ReadFromJsonAsync<StatsDto>(_jsonOptions);
        Assert.NotNull(stats);
        Assert.NotNull(stats.FromUtc);
        Assert.Equal(1, stats.FromUtc.Value.Day);
        Assert.Equal(0, stats.FromUtc.Value.Hour);
        Assert.Equal(DateTime.UtcNow.Month, stats.FromUtc.Value.Month);
        Assert.Equal(DateTimeKind.Utc, DateTime.SpecifyKind(stats.FromUtc.Value, DateTimeKind.Utc).Kind);
    }

    [Fact]
    public async Task GetStats_UnknownPeriod_Returns400()
    {
        using var factory = new StatsSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/master/stats?period=Week");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
