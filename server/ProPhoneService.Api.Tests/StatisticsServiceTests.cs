using Moq;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

/// <summary>
/// Проверка границы периода и агрегатов статистики дашборда
/// </summary>
public class StatisticsServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static (StatisticsService Service, Mock<IRepairOrderRepository> Repository) CreateService(
        DateTimeOffset nowUtc,
        IReadOnlyDictionary<DeviceType, int>? counts = null,
        decimal total = 0)
    {
        var repository = new Mock<IRepairOrderRepository>();
        repository
            .Setup(r => r.CountCompletedByDeviceTypeAsync(
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(counts ?? new Dictionary<DeviceType, int>());
        repository
            .Setup(r => r.SumCompletedOrdersAsync(
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(total);

        return (new StatisticsService(repository.Object, new FixedTimeProvider(nowUtc)), repository);
    }

    [Fact]
    public async Task Get_MonthPeriod_StartsAtFirstDayOfMonth()
    {
        var now = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        var (service, repository) = CreateService(now);

        var stats = await service.GetAsync(StatsPeriod.Month);

        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), stats.FromUtc);
        Assert.Equal(now.UtcDateTime, stats.ToUtc);
        repository.Verify(r => r.SumCompletedOrdersAsync(
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            now.UtcDateTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(2026, 8, 15, 2026, 7, 1)]
    [InlineData(2026, 10, 31, 2026, 10, 1)]
    [InlineData(2027, 2, 1, 2027, 1, 1)]
    public async Task Get_QuarterPeriod_StartsAtFirstDayOfQuarter(
        int year, int month, int day, int expectedYear, int expectedMonth, int expectedDay)
    {
        var now = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);
        var (service, _) = CreateService(now);

        var stats = await service.GetAsync(StatsPeriod.Quarter);

        Assert.Equal(new DateTime(expectedYear, expectedMonth, expectedDay, 0, 0, 0, DateTimeKind.Utc), stats.FromUtc);
    }

    [Fact]
    public async Task Get_AllPeriod_HasNoLowerBound()
    {
        var now = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        var (service, repository) = CreateService(now);

        var stats = await service.GetAsync(StatsPeriod.All);

        Assert.Null(stats.FromUtc);
        repository.Verify(r => r.CountCompletedByDeviceTypeAsync(
            null,
            now.UtcDateTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_ReturnsTotalAndCountsSortedByCountDescending()
    {
        var now = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        var counts = new Dictionary<DeviceType, int>
        {
            [DeviceType.Phone] = 2,
            [DeviceType.Tablet] = 3,
            [DeviceType.Laptop] = 3,
        };
        var (service, _) = CreateService(now, counts, total: 12500.50m);

        var stats = await service.GetAsync(StatsPeriod.Month);

        Assert.Equal(12500.50m, stats.CompletedTotalPrice);
        Assert.Collection(
            stats.CompletedByDeviceType,
            x => Assert.Equal((DeviceType.Tablet, 3), (x.Type, x.Count)),
            x => Assert.Equal((DeviceType.Laptop, 3), (x.Type, x.Count)),
            x => Assert.Equal((DeviceType.Phone, 2), (x.Type, x.Count)));
    }

    [Fact]
    public async Task Get_NoCompletedRepairs_ReturnsZeroAndEmptyCounts()
    {
        var (service, _) = CreateService(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

        var stats = await service.GetAsync(StatsPeriod.All);

        Assert.Equal(0m, stats.CompletedTotalPrice);
        Assert.Empty(stats.CompletedByDeviceType);
    }
}
