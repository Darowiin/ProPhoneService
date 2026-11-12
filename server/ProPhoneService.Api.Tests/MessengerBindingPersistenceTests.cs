using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Infrastructure.EfCore;
using ProPhoneService.Infrastructure.EfCore.Repositories;

namespace ProPhoneService.Api.Tests;

public class MessengerBindingPersistenceTests
{
    [PostgresFact]
    public async Task ConcurrentCallbacks_OnlyOneRecipientCanConsumeLink()
    {
        await using var database = await Database.CreateAsync();
        var hash = Guid.NewGuid().ToString("N");
        await database.SeedAsync(hash);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(1, 8).Select(async recipient =>
        {
            await using var context = database.Context();
            await start.Task;
            return await new MessengerBindingRepository(context).TryBindAsync(hash, MessengerChannel.Telegram,
                recipient.ToString(), DateTime.UtcNow);
        }).ToArray();

        start.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.Single(results, value => value);
        await using var check = database.Context();
        var saved = await check.Set<MessengerBinding>().SingleAsync();
        Assert.NotNull(saved.RecipientId);
        Assert.NotNull(saved.LinkedAt);
    }

    [PostgresFact]
    public async Task NewGuestOrder_CannotReplaceExistingRecipientForSamePhone()
    {
        await using var database = await Database.CreateAsync();
        await database.SeedAsync("first");
        await database.SeedAsync("second");
        await using var first = database.Context();
        var firstRepository = new MessengerBindingRepository(first);
        Assert.True(await firstRepository.TryBindAsync("first", MessengerChannel.Telegram, "42", DateTime.UtcNow));
        await using var second = database.Context();
        var secondRepository = new MessengerBindingRepository(second);

        var result = await secondRepository.TryBindAsync("second", MessengerChannel.Telegram, "99", DateTime.UtcNow);

        Assert.False(result);
        Assert.Equal("42", (await secondRepository.GetByPhoneAsync("+79001234567"))!.RecipientId);
    }

    [PostgresFact]
    public async Task ExpiredLinkAndWrongChannel_AreRejectedWithoutChanges()
    {
        await using var database = await Database.CreateAsync();
        await database.SeedAsync("expired", DateTime.UtcNow.AddMinutes(-1));
        await database.SeedAsync("valid");
        await using var context = database.Context();
        var repository = new MessengerBindingRepository(context);

        var expired = await repository.TryBindAsync("expired", MessengerChannel.Telegram, "42", DateTime.UtcNow);
        var wrongChannel = await repository.TryBindAsync("valid", MessengerChannel.Vk, "42", DateTime.UtcNow);

        Assert.False(expired);
        Assert.False(wrongChannel);
        Assert.All(await context.Set<MessengerBinding>().ToListAsync(), value => Assert.Null(value.RecipientId));
    }

    public sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROPHONE_TEST_POSTGRES_CONNECTION")))
                Skip = "Set PROPHONE_TEST_POSTGRES_CONNECTION to run isolated PostgreSQL tests";
        }
    }

    private sealed class Database(string adminConnection, string name, string connection) : IAsyncDisposable
    {
        public ProPhoneServiceDbContext Context() => new(new DbContextOptionsBuilder<ProPhoneServiceDbContext>()
            .UseNpgsql(connection).Options);

        public static async Task<Database> CreateAsync()
        {
            var adminConnection = Environment.GetEnvironmentVariable("PROPHONE_TEST_POSTGRES_CONNECTION")!;
            var name = "messenger_test_" + Guid.NewGuid().ToString("N");
            await using var admin = new NpgsqlConnection(adminConnection);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
            var builder = new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = false };
            var database = new Database(adminConnection, name, builder.ConnectionString);
            try
            {
                await using var context = database.Context();
                await context.Database.MigrateAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async Task SeedAsync(string hash, DateTime? expiresAt = null)
        {
            await using var context = Context();
            var device = new Device { Id = Guid.NewGuid(), Type = DeviceType.Phone, Manufacturer = "Test", Model = "Test" };
            var order = RepairOrder.CreatePending(device.Id, "legacy@example.com", "+79001234567");
            context.AddRange(device, order, new MessengerBinding { Id = Guid.NewGuid(), TokenHash = hash,
                Channel = MessengerChannel.Telegram, Phone = "+79001234567", OrderId = order.Id,
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(5) });
            await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
