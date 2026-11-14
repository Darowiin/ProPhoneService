using Microsoft.EntityFrameworkCore;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Infrastructure.EfCore;
using static ProPhoneService.Api.Tests.MessengerBindingPersistenceTests;

namespace ProPhoneService.Api.Tests;

public class OrderNumberPersistenceTests
{
    [PostgresFact]
    public async Task ConcurrentCreates_UseOneSequenceWithoutDuplicateNumbers()
    {
        await using var database = await Database.CreateAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = database.Context();
            await start.Task;
            return await CreateAsync(db);
        }).ToArray();

        start.SetResult();
        var orders = await Task.WhenAll(tasks);

        Assert.Equal(12, orders.Select(order => order.OrderNumber).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 12).Select(x => (long)x), orders.Select(x => x.SequenceNumber).Order());
        Assert.All(orders, order => Assert.Matches(@"^З-\d{6}$", order.OrderNumber));
    }

    [PostgresFact]
    public async Task NumberSurvivesStatusChangeAndReload_DeletedNumbersAreNotReused()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var first = await CreateAsync(db);
        var id = first.Id;
        first.ChangeStatus(RepairStatus.Cancelled);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var restored = await db.RepairOrders.SingleAsync(x => x.Id == id);
        var second = await CreateAsync(db);
        db.Remove(restored);
        await db.SaveChangesAsync();
        var third = await CreateAsync(db);

        Assert.Equal("З-000001", restored.OrderNumber);
        Assert.Equal(RepairStatus.Cancelled, restored.Status);
        Assert.Equal("З-000002", second.OrderNumber);
        Assert.Equal("З-000003", third.OrderNumber);
        await using var anotherContext = database.Context();
        Assert.Equal(third.OrderNumber, (await anotherContext.RepairOrders.SingleAsync(x => x.Id == third.Id)).OrderNumber);
    }

    [PostgresFact]
    public async Task FormatBeyondSixDigits_DoesNotTruncateOrRepeat()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        await db.Database.ExecuteSqlRawAsync("SELECT setval('repair_order_number_seq', 999999, false)");

        var first = await CreateAsync(db);
        var next = await CreateAsync(db);

        Assert.Equal("З-999999", first.OrderNumber);
        Assert.Equal("З-1000000", next.OrderNumber);
    }

    private static async Task<RepairOrder> CreateAsync(ProPhoneServiceDbContext db)
    {
        var device = new Device { Id = Guid.NewGuid(), Type = DeviceType.Phone, Manufacturer = "Test", Model = "Test" };
        var order = RepairOrder.CreatePending(device.Id, "+79001234567");
        order.CreatedAt = DateTime.UtcNow;
        db.AddRange(device, order);
        await db.SaveChangesAsync();
        return order;
    }
}
