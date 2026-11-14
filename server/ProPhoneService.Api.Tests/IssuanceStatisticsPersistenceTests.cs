using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Auditing;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Infrastructure.EfCore;
using ProPhoneService.Infrastructure.EfCore.Auditing;
using ProPhoneService.Infrastructure.EfCore.Repositories;
using static ProPhoneService.Api.Tests.MessengerBindingPersistenceTests;

namespace ProPhoneService.Api.Tests;

public class IssuanceStatisticsPersistenceTests
{
    private class Clock : TimeProvider
    {
        public DateTime UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Issuance_WithAndWithoutRepair_PersistsIssueDateAndNotifiesOnlyReady(bool repaired)
    {
        await using var database = await Database.CreateAsync();
        var clock = new Clock { UtcNow = Utc(2026, 11, 14) };
        var actorId = Guid.NewGuid();
        var actor = Mock.Of<ICurrentActor>(x => x.AccountId == actorId);
        await using var db = database.Context(new AuditInterceptor(clock, actor));
        var order = await CreateAsync(db, clock, clock.UtcNow, DeviceType.Phone, 1250m);
        var account = new Account { Id = Guid.NewGuid(), ClientId = order.ClientId!.Value };
        db.AddRange(account, new MessengerBinding
        {
            Id = Guid.NewGuid(), TokenHash = "hash", Phone = "+79001234567", Channel = MessengerChannel.Vk,
            AccountId = account.Id, RecipientId = "42", LinkedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync();
        if (repaired)
        {
            order.ChangeStatus(RepairStatus.InProgress);
            await db.SaveChangesAsync();
        }
        var messages = new List<string>();
        var notifier = new Mock<INotifier>();
        notifier.Setup(x => x.SendAsync(MessengerChannel.Vk, "42", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((MessengerChannel _, string _, string text, CancellationToken _) => messages.Add(text))
            .Returns(Task.CompletedTask);
        var notifications = new StatusNotificationService(notifier.Object, new EfRepository<NotificationLog>(db),
            new NotificationOptions { RetryDelay = TimeSpan.Zero }, NullLogger<StatusNotificationService>.Instance,
            new MessengerBindingRepository(db));
        var workflow = new OrderWorkflowService(new RepairOrderRepository(db), notifications, Mock.Of<IMapper>(),
            NullLogger<OrderWorkflowService>.Instance);
        var readyAt = Utc(2026, 11, 15);
        var issuedAt = readyAt.AddDays(2).AddHours(1);

        clock.UtcNow = readyAt;
        var ready = await workflow.ChangeStatusAsync(order.Id, RepairStatus.Ready);
        clock.UtcNow = issuedAt;
        var completed = await workflow.ChangeStatusAsync(order.Id, RepairStatus.Completed);
        var repeat = await workflow.ChangeStatusAsync(order.Id, RepairStatus.Completed);
        db.ChangeTracker.Clear();

        Assert.Equal(ChangeStatusResult.Success, ready);
        Assert.Equal(ChangeStatusResult.Success, completed);
        Assert.Equal(ChangeStatusResult.InvalidTransition, repeat);
        var saved = await db.RepairOrders.Include(x => x.StatusHistory).SingleAsync(x => x.Id == order.Id);
        Assert.Equal(RepairStatus.Completed, saved.Status);
        Assert.Equal(1250m, saved.TotalPrice);
        Assert.Equal(repaired, saved.StatusHistory.Any(x => x.Status == RepairStatus.InProgress));
        Assert.Equal(readyAt, Assert.Single(saved.StatusHistory, x => x.Status == RepairStatus.Ready).ChangedAt);
        var issued = Assert.Single(saved.StatusHistory, x => x.Status == RepairStatus.Completed);
        Assert.Equal(issuedAt, issued.ChangedAt);
        Assert.Equal(actorId, issued.ChangedBy);
        Assert.Equal("Выдан клиенту", RepairStatusNames.GetName(saved.Status));
        Assert.StartsWith("Готов к выдаче", Assert.Single(messages));
        Assert.Single(await db.NotificationLogs.ToListAsync());
    }

    [PostgresTheory]
    [InlineData(StatsPeriod.Month, 2026, 11, 14, 11)]
    [InlineData(StatsPeriod.Month, 2027, 1, 1, 1)]
    [InlineData(StatsPeriod.Quarter, 2026, 11, 14, 10)]
    [InlineData(StatsPeriod.Quarter, 2027, 2, 1, 1)]
    public async Task PeriodStats_UseIssueDateAndInclusiveBoundaries(StatsPeriod period, int year, int month, int day, int startMonth)
    {
        await using var database = await Database.CreateAsync();
        var clock = new Clock();
        await using var db = database.Context(new AuditInterceptor(clock, Mock.Of<ICurrentActor>()));
        var start = Utc(year, startMonth, 1);
        var now = Utc(year, month, day).AddHours(12);
        var before = await CreateAsync(db, clock, start.AddYears(-1), DeviceType.Phone, 10m);
        await IssueAsync(db, clock, before, start.AddTicks(-10), repaired: true);
        var lowerBoundary = await CreateAsync(db, clock, start.AddMonths(-2), DeviceType.Phone, 20m);
        await IssueAsync(db, clock, lowerBoundary, start, repaired: false);
        var upperBoundary = await CreateAsync(db, clock, start, DeviceType.Tablet, 30m);
        await IssueAsync(db, clock, upperBoundary, now, repaired: true);
        var zeroPrice = await CreateAsync(db, clock, start.AddDays(-1), DeviceType.Laptop, 0m);
        await IssueAsync(db, clock, zeroPrice, now, repaired: false);
        var after = await CreateAsync(db, clock, start.AddDays(-1), DeviceType.Laptop, 40m);
        await IssueAsync(db, clock, after, now.AddTicks(10), repaired: false);
        var readyOnly = await CreateAsync(db, clock, start, DeviceType.Phone, 1000m);
        readyOnly.ChangeStatus(RepairStatus.Ready);
        await db.SaveChangesAsync();
        var cancelled = await CreateAsync(db, clock, start, DeviceType.Phone, 2000m);
        cancelled.ChangeStatus(RepairStatus.Cancelled);
        await db.SaveChangesAsync();
        await CreateAsync(db, clock, start, DeviceType.Phone, 3000m);
        clock.UtcNow = now;
        var service = new StatisticsService(new RepairOrderRepository(db), clock);

        var stats = await service.GetAsync(period);

        Assert.Equal(start, stats.FromUtc);
        Assert.Equal(now, stats.ToUtc);
        Assert.Equal(50m, stats.CompletedTotalPrice);
        Assert.Equal(3, stats.CompletedByDeviceType.Sum(x => x.Count));
        Assert.All(stats.CompletedByDeviceType, item => Assert.Equal(1, item.Count));
        Assert.Equal(new[] { DeviceType.Phone, DeviceType.Tablet, DeviceType.Laptop },
            stats.CompletedByDeviceType.Select(x => x.Type).Order());
    }

    [PostgresFact]
    public async Task AllTimeStats_IncludeOldIssuedOrdersButExcludeFutureIssueDates()
    {
        await using var database = await Database.CreateAsync();
        var clock = new Clock();
        await using var db = database.Context(new AuditInterceptor(clock, Mock.Of<ICurrentActor>()));
        var now = Utc(2026, 11, 14);
        var old = await CreateAsync(db, clock, now.AddYears(-5), DeviceType.Phone, 100m);
        await IssueAsync(db, clock, old, now.AddYears(-4), repaired: false);
        var current = await CreateAsync(db, clock, now.AddDays(-10), DeviceType.Phone, 200m);
        await IssueAsync(db, clock, current, now, repaired: true);
        var future = await CreateAsync(db, clock, now.AddDays(-1), DeviceType.Phone, 400m);
        await IssueAsync(db, clock, future, now.AddTicks(10), repaired: false);
        clock.UtcNow = now;
        var service = new StatisticsService(new RepairOrderRepository(db), clock);

        var stats = await service.GetAsync(StatsPeriod.All);

        Assert.Null(stats.FromUtc);
        Assert.Equal(300m, stats.CompletedTotalPrice);
        Assert.Equal(2, Assert.Single(stats.CompletedByDeviceType).Count);
    }

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<RepairOrder> CreateAsync(ProPhoneServiceDbContext db, Clock clock, DateTime createdAt,
        DeviceType type, decimal price)
    {
        clock.UtcNow = createdAt;
        var device = new Device { Id = Guid.NewGuid(), Type = type, Manufacturer = "Test", Model = "Test" };
        var client = new Client { Id = Guid.NewGuid(), Name = "Test" };
        var order = RepairOrder.Create(client.Id, device.Id);
        order.TotalPrice = price;
        db.AddRange(device, client, order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task IssueAsync(ProPhoneServiceDbContext db, Clock clock, RepairOrder order, DateTime issuedAt, bool repaired)
    {
        clock.UtcNow = issuedAt.AddMinutes(-1);
        if (repaired)
        {
            order.ChangeStatus(RepairStatus.InProgress);
            await db.SaveChangesAsync();
        }
        order.ChangeStatus(RepairStatus.Ready);
        await db.SaveChangesAsync();
        clock.UtcNow = issuedAt;
        order.ChangeStatus(RepairStatus.Completed);
        await db.SaveChangesAsync();
    }
}
