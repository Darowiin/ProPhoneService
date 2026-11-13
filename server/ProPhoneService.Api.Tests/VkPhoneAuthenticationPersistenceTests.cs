using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Infrastructure.EfCore;
using ProPhoneService.Infrastructure.EfCore.Repositories;
using static ProPhoneService.Api.Tests.MessengerBindingPersistenceTests;

namespace ProPhoneService.Api.Tests;

public class VkPhoneAuthenticationPersistenceTests
{
    private const string Phone = "+79001234567";

    private class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Flow(ProPhoneServiceDbContext db, Clock clock)
    {
        public List<string> Messages { get; } = [];
        public bool FailDelivery { get; set; }

        public MessengerBindingService Bindings => new(new MessengerBindingRepository(db),
            new RepairOrderRepository(db), new AccountRepository(db), new ClientRepository(db),
            MessengerDeliveryTests.Options(), clock);

        public AuthService Auth => new(new AccountRepository(db), new ClientRepository(db), new AuthCodeRepository(db),
            new RepairOrderRepository(db), Notifier(), clock, new AuthOptions(), new JwtOptions
            {
                Key = new string('x', 48), Issuer = "tests", Audience = "tests",
            }, new InMemoryRefreshTokenStore(clock), new MessengerBindingRepository(db));

        public INotifier Notifier()
        {
            var notifier = new Mock<INotifier>();
            notifier.Setup(x => x.SendAsync(MessengerChannel.Vk, "42", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((MessengerChannel _, string _, string text, CancellationToken _) =>
                {
                    if (FailDelivery) throw new MessengerUnavailableException(MessengerChannel.Vk);
                    Messages.Add(text);
                    return Task.CompletedTask;
                });
            return notifier.Object;
        }

        public async Task<(RepairOrder Order, string Token)> PendingAsync(string name = "Иван")
        {
            var device = new Device { Id = Guid.NewGuid(), Manufacturer = "Test", Model = "Test", Type = DeviceType.Phone };
            var order = RepairOrder.CreatePending(device.Id, Phone);
            order.ContactName = name;
            order.CreatedAt = clock.GetUtcNow().UtcDateTime;
            db.AddRange(device, order);
            await db.SaveChangesAsync();
            var link = await Bindings.CreateForOrderAsync(order.Id, MessengerChannel.Vk);
            return (order, link!.Url.Split("ref=")[1]);
        }

        public async Task Callback(string token)
        {
            // Callback приходит в новом scope после HTTP-запроса создания заявки
            db.ChangeTracker.Clear();
            var handler = new VkUpdateHandler(Bindings, Notifier(), MessengerDeliveryTests.Options(), Auth);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                type = "message_new", group_id = 123,
                @object = new { message = new { from_id = 42, peer_id = 42, @ref = token } },
            }));
            await handler.HandleAsync(json.RootElement);
        }
    }

    [PostgresFact]
    public async Task VkCallback_CodeConfirmationAndRepeatLogin_KeepSameAccountAndBinding()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var clock = new Clock();
        var flow = new Flow(db, clock);
        var (order, token) = await flow.PendingAsync();

        await flow.Callback(token);
        var code = await db.Set<AuthCode>().SingleAsync();
        var first = await flow.Auth.ConfirmAsync(Phone, code.Code);
        var replay = await flow.Auth.ConfirmAsync(Phone, code.Code);
        await flow.Auth.RequestCodeAsync(Phone);
        var nextCode = await db.Set<AuthCode>().SingleAsync();
        var second = await flow.Auth.ConfirmAsync(Phone, nextCode.Code);

        Assert.Equal(200, first.StatusCode);
        Assert.Equal(400, replay.StatusCode);
        Assert.Equal(first.Tokens!.AccountId, second.Tokens!.AccountId);
        Assert.Equal(RepairStatus.Created, (await db.Set<RepairOrder>().SingleAsync(x => x.Id == order.Id)).Status);
        Assert.Equal(first.Tokens.ClientId, (await db.Set<RepairOrder>().SingleAsync(x => x.Id == order.Id)).ClientId);
        Assert.Single(await db.Set<Client>().ToListAsync());
        Assert.Single(await db.Set<Account>().ToListAsync());
        var binding = await db.Set<MessengerBinding>().SingleAsync();
        Assert.Null(binding.OrderId);
        Assert.Equal(first.Tokens.AccountId, binding.AccountId);
        Assert.Contains(flow.Messages, message => message.Contains("Мессенджер успешно привязан!"));
        Assert.Contains(flow.Messages, message => Regex.IsMatch(message, @"Ваш код подтверждения: \d{6}"));
    }

    [PostgresFact]
    public async Task Confirmation_ActivatesOnlyLinkedOrderAndPreservesExistingName()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var clock = new Clock();
        var flow = new Flow(db, clock);
        var (first, token) = await flow.PendingAsync("Первое имя");
        await flow.Callback(token);
        var code = await db.Set<AuthCode>().SingleAsync();
        await flow.Auth.ConfirmAsync(Phone, code.Code);
        var (second, secondToken) = await flow.PendingAsync("Другое имя");
        var (unbound, _) = await flow.PendingAsync("Без привязки");

        await flow.Callback(secondToken);
        var secondCode = await db.Set<AuthCode>().SingleAsync();
        var result = await flow.Auth.ConfirmAsync(Phone, secondCode.Code);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(RepairStatus.Created, (await db.Set<RepairOrder>().SingleAsync(x => x.Id == first.Id)).Status);
        Assert.Equal(RepairStatus.Created, (await db.Set<RepairOrder>().SingleAsync(x => x.Id == second.Id)).Status);
        Assert.Equal(RepairStatus.PendingContact, unbound.Status);
        Assert.Equal("Первое имя", (await db.Set<Client>().SingleAsync()).Name);
    }

    [PostgresFact]
    public async Task Expiration_CancelsOnlyPendingOrdersWithHistoryAndNoNotification()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var clock = new Clock();
        var flow = new Flow(db, clock);
        var (expired, token) = await flow.PendingAsync();
        await flow.Callback(token);
        var code = await db.Set<AuthCode>().SingleAsync();
        clock.Now = clock.Now.AddMinutes(5);
        var (fresh, _) = await flow.PendingAsync();
        var messagesBefore = flow.Messages.Count;
        var orders = new RepairOrderRepository(db);

        var confirmed = await flow.Auth.ConfirmAsync(Phone, code.Code);
        var cancelled = await orders.CancelExpiredPendingAsync(clock.Now.UtcDateTime);
        var again = await orders.CancelExpiredPendingAsync(clock.Now.UtcDateTime);
        db.ChangeTracker.Clear();

        Assert.Equal(400, confirmed.StatusCode);
        Assert.Equal(1, cancelled);
        Assert.Equal(0, again);
        Assert.Equal(RepairStatus.Cancelled, (await orders.GetByIdAsync(expired.Id))!.Status);
        Assert.Equal(RepairStatus.PendingContact, (await orders.GetByIdAsync(fresh.Id))!.Status);
        Assert.Single(await db.Set<RepairStatusHistory>().Where(x => x.RepairOrderId == expired.Id
            && x.Status == RepairStatus.Cancelled).ToListAsync());
        Assert.Equal(messagesBefore, flow.Messages.Count);
    }

    [PostgresFact]
    public async Task LateCodeCannotActivateOrderEvenWhenCodeHasNotExpired()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var clock = new Clock();
        var flow = new Flow(db, clock);
        var (_, token) = await flow.PendingAsync();
        clock.Now = clock.Now.AddMinutes(4);
        await flow.Callback(token);
        var code = await db.Set<AuthCode>().SingleAsync();
        clock.Now = clock.Now.AddMinutes(1);

        var result = await flow.Auth.ConfirmAsync(Phone, code.Code);

        Assert.True(code.ExpiresAt > clock.Now.UtcDateTime);
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(await db.Set<Account>().ToListAsync());
    }

    [PostgresFact]
    public async Task DeliveryFailure_DoesNotStartCooldownAndCallbackCanBeRetried()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var flow = new Flow(db, new Clock());
        var (_, token) = await flow.PendingAsync();
        Assert.True(await flow.Bindings.BindAsync(MessengerChannel.Vk, token, 42));
        flow.FailDelivery = true;

        await Assert.ThrowsAsync<MessengerUnavailableException>(() => flow.Auth.RequestCodeAsync(Phone));
        Assert.Empty(await db.Set<AuthCode>().ToListAsync());
        flow.FailDelivery = false;
        var retry = await flow.Auth.RequestCodeAsync(Phone);
        var cooldown = await flow.Auth.RequestCodeAsync(Phone);

        Assert.Equal(204, retry.StatusCode);
        Assert.Equal(429, cooldown.StatusCode);
        Assert.Single(await db.Set<AuthCode>().ToListAsync());
    }

    [PostgresFact]
    public async Task ConcurrentConfirmation_ConsumesCodeAndCreatesAccountOnlyOnce()
    {
        await using var database = await Database.CreateAsync();
        var clock = new Clock();
        string code;
        await using (var db = database.Context())
        {
            var flow = new Flow(db, clock);
            var (_, token) = await flow.PendingAsync();
            await flow.Callback(token);
            code = (await db.Set<AuthCode>().SingleAsync()).Code;
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = database.Context();
            await start.Task;
            return await new Flow(db, clock).Auth.ConfirmAsync(Phone, code);
        }).ToArray();

        start.SetResult();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, x => x.StatusCode == 200);
        Assert.Equal(3, results.Count(x => x.StatusCode == 400));
        await using var check = database.Context();
        Assert.Single(await check.Set<Client>().ToListAsync());
        Assert.Single(await check.Set<Account>().ToListAsync());
        Assert.Empty(await check.Set<AuthCode>().ToListAsync());
    }

    [PostgresFact]
    public async Task Migration_InvalidatesLegacyEmailCodesAndPreservesClients()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261112155352_AlignMessengerBindingColumns");
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO client (id, name, phone, email, created_at)
            VALUES ({id}, 'Existing', {Phone}, 'legacy@example.com', now());
            INSERT INTO auth_code (id, email, code, expires_at, created_at, failed_attempts)
            VALUES (gen_random_uuid(), 'legacy@example.com', '123456', now() + interval '5 minutes', now(), 0)
            """);

        await migrator.MigrateAsync();

        Assert.Empty(await db.Set<AuthCode>().ToListAsync());
        var client = await db.Set<Client>().SingleAsync();
        Assert.Equal(id, client.Id);
        Assert.Equal(Phone, client.Phone);
    }

    [PostgresFact]
    public async Task MasterBootstrap_CreatesTrustedVkBindingAndDoesNotDuplicateIt()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var seeder = new MasterAccountSeeder(new ClientRepository(db), new AccountRepository(db),
            new MessengerBindingRepository(db), new AuthOptions { MasterPhone = Phone, MasterVkUserId = 42 },
            NullLogger<MasterAccountSeeder>.Instance);

        var first = await seeder.EnsureAsync();
        var second = await seeder.EnsureAsync();

        Assert.True(first);
        Assert.False(second);
        Assert.Single(await db.Set<Client>().ToListAsync());
        var account = await db.Set<Account>().SingleAsync();
        Assert.Equal(AccountRole.Master, account.Role);
        var binding = await db.Set<MessengerBinding>().SingleAsync();
        Assert.Equal(account.Id, binding.AccountId);
        Assert.Equal("42", binding.RecipientId);
    }

    [PostgresFact]
    public async Task LoginThroughAccountBinding_FillsMissingPhoneWithoutChangingName()
    {
        await using var database = await Database.CreateAsync();
        await using var db = database.Context();
        var flow = new Flow(db, new Clock());
        var (_, token) = await flow.PendingAsync("Исходное имя");
        await flow.Callback(token);
        var firstCode = await db.Set<AuthCode>().SingleAsync();
        var first = await flow.Auth.ConfirmAsync(Phone, firstCode.Code);
        var client = await db.Set<Client>().SingleAsync();
        client.Phone = null;
        await db.SaveChangesAsync();
        await flow.Auth.RequestCodeAsync(Phone);
        var nextCode = await db.Set<AuthCode>().SingleAsync();

        var result = await flow.Auth.ConfirmAsync(Phone, nextCode.Code);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(first.Tokens!.AccountId, result.Tokens!.AccountId);
        Assert.Equal(Phone, client.Phone);
        Assert.Equal("Исходное имя", client.Name);
        Assert.Single(await db.Set<Client>().ToListAsync());
    }
}
