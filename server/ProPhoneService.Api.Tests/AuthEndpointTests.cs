using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class AuthEndpointTests
{
    /// <summary>
    /// Понедельник 2026-10-05 08:00 UTC = 12:00 по Самаре
    /// </summary>
    private static readonly DateTimeOffset _nowUtc = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private const string Phone = "+79001234567";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => _nowUtc;
    }

    /// <summary>
    /// Mock-хранилища auth-сущностей + перехват отправителя сообщений
    /// </summary>
    private class AuthStore
    {
        public long TrustedMasterVkId { get; set; } = 42;

        public List<Client> Clients { get; set; } = [];

        public List<Account> Accounts { get; set; } = [];

        // Словарь имитирует хранение по phone (один активный код на адрес)
        public Dictionary<string, AuthCode> AuthCodes { get; set; } = [];

        public List<(string Target, string Subject, string Text)> Notifications { get; } = [];

        public List<RepairOrder> PendingOrders { get; } = [];

        public List<Client> AddedClients { get; } = [];

        public List<Account> AddedAccounts { get; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("Database:MigrateOnStartup", "false");
                host.ConfigureTestServices(services =>
                {
                    foreach (var order in PendingOrders) order.CreatedAt = _nowUtc.UtcDateTime;
                    var binding = new MessengerBinding
                    {
                        Id = Guid.NewGuid(), TokenHash = "hash", Phone = Phone, Channel = MessengerChannel.Vk,
                        RecipientId = "42", LinkedAt = _nowUtc.UtcDateTime, ExpiresAt = _nowUtc.UtcDateTime.AddMinutes(5),
                        OrderId = PendingOrders.FirstOrDefault()?.Id,
                        AccountId = PendingOrders.Count == 0 ? Accounts.FirstOrDefault()?.Id : null,
                    };
                    var bindings = new Mock<IMessengerBindingRepository>();
                    bindings.Setup(x => x.GetByPhoneAsync(Phone, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
                    bindings.Setup(x => x.GetByIdAsync(binding.Id, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
                    bindings.Setup(x => x.GetByClientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(binding);
                    services.RemoveAll<IRepository<NotificationLog>>();
                    services.AddSingleton(Mock.Of<IRepository<NotificationLog>>());
                    services.RemoveAll<IMessengerBindingRepository>();
                    services.AddSingleton(bindings.Object);
                    services.AddSingleton(new AuthOptions { MasterVkUserId = TrustedMasterVkId });

                    var authCodeRepository = new Mock<IAuthCodeRepository>();
                    authCodeRepository
                        .Setup(r => r.ExecuteForPhoneAsync(It.IsAny<string>(), It.IsAny<Func<Task<AuthOutcome>>>(), It.IsAny<CancellationToken>()))
                        .Returns((string _, Func<Task<AuthOutcome>> operation, CancellationToken _) => operation());
                    authCodeRepository
                        .Setup(r => r.GetByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string phone, CancellationToken _) => AuthCodes.GetValueOrDefault(phone));

                    authCodeRepository
                        .Setup(r => r.AddAsync(It.IsAny<AuthCode>(), It.IsAny<CancellationToken>()))
                        .Callback((AuthCode code, CancellationToken _) => AuthCodes[code.Phone] = code)
                        .Returns(Task.CompletedTask);

                    authCodeRepository
                        .Setup(r => r.Update(It.IsAny<AuthCode>()))
                        .Callback((AuthCode code) => AuthCodes[code.Phone] = code);

                    authCodeRepository
                        .Setup(r => r.Remove(It.IsAny<AuthCode>()))
                        .Callback((AuthCode code) => AuthCodes.Remove(code.Phone));

                    authCodeRepository
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    var clientRepository = new Mock<IClientRepository>();
                    clientRepository
                        .Setup(r => r.GetByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string phone, CancellationToken _) =>
                            Clients.FirstOrDefault(c => c.Phone == phone));

                    clientRepository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) => Clients.FirstOrDefault(x => x.Id == id));
                    clientRepository
                        .Setup(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
                        .Callback((Client c, CancellationToken _) =>
                        {
                            Clients.Add(c);
                            AddedClients.Add(c);
                        })
                        .Returns(Task.CompletedTask);

                    clientRepository
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    var accountRepository = new Mock<IAccountRepository>();
                    accountRepository
                        .Setup(r => r.GetByClientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid clientId, CancellationToken _) =>
                            Accounts.FirstOrDefault(a => a.ClientId == clientId));

                    accountRepository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) => Accounts.FirstOrDefault(x => x.Id == id));
                    accountRepository
                        .Setup(r => r.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()))
                        .Callback((Account a, CancellationToken _) =>
                        {
                            Accounts.Add(a);
                            AddedAccounts.Add(a);
                        })
                        .Returns(Task.CompletedTask);

                    var orderRepository = new Mock<IRepairOrderRepository>();
                    orderRepository
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    orderRepository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) => PendingOrders.FirstOrDefault(x => x.Id == id));
                    orderRepository.Setup(x => x.GetWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) => PendingOrders.FirstOrDefault(x => x.Id == id));

                    var notifier = new Mock<INotifier>();
                    notifier
                        .Setup(n => n.SendAsync(
                            It.IsAny<MessengerChannel>(),
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<CancellationToken>()))
                        .Callback((MessengerChannel channel, string target, string text, CancellationToken _) =>
                            Notifications.Add((target, channel.ToString(), text)))
                        .Returns(Task.CompletedTask);

                    services.RemoveAll<IAuthCodeRepository>();
                    services.AddScoped(_ => authCodeRepository.Object);
                    services.RemoveAll<IClientRepository>();
                    services.AddScoped(_ => clientRepository.Object);
                    services.RemoveAll<IAccountRepository>();
                    services.AddScoped(_ => accountRepository.Object);
                    services.RemoveAll<INotifier>();
                    services.AddScoped(_ => notifier.Object);
                    services.RemoveAll<IRepairOrderRepository>();
                    services.AddScoped(_ => orderRepository.Object);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider());
                });
            });
        }
    }

    private static Task<HttpResponseMessage> RequestCode(HttpClient client, string phone)
    {
        return client.PostAsJsonAsync("/api/auth/request-code", new { phone });
    }

    private static Task<HttpResponseMessage> Confirm(HttpClient client, string phone, string code)
    {
        return client.PostAsJsonAsync("/api/auth/confirm", new { phone, code });
    }

    [Fact]
    public async Task RequestCode_ValidPhone_SendsCodeThroughNotifier()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();

        // Phone нормализуется (trim + нижний регистр)
        var response = await RequestCode(client, "  +79001234567 ");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var sent = Assert.Single(store.Notifications);
        Assert.Equal("42", sent.Target);

        // Код - 6 цифр и совпадает с сохранённым
        var match = Regex.Match(sent.Text, @"\b\d{6}\b");
        Assert.True(match.Success);
        var savedCode = store.AuthCodes[Phone];
        Assert.Equal(match.Value, savedCode.Code);
        Assert.Equal(_nowUtc.UtcDateTime.AddMinutes(5), savedCode.ExpiresAt);
    }

    [Fact]
    public async Task RequestCode_EmailIsNotAccepted_Returns400()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();

        var response = await RequestCode(client, "email@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(store.Notifications);
    }

    [Fact]
    public async Task RequestCode_InvalidPhone_Returns400()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();

        var response = await RequestCode(client, "абв");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(store.Notifications);
    }

    [Fact]
    public async Task RequestCode_Cooldown_Returns429OnSecondRequest()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();

        await RequestCode(client, Phone);
        var second = await RequestCode(client, Phone);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Single(store.Notifications);
    }

    [Fact]
    public async Task Confirm_UnknownPhone_Returns400()
    {
        using var client = new AuthStore().CreateFactory().CreateClient();

        var response = await Confirm(client, Phone, "123456");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_WrongCodeFiveTimes_LocksPhone()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Phone);
        var wrongCode = store.AuthCodes[Phone].Code == "000000" ? "000001" : "000000";

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await Confirm(client, Phone, wrongCode);

            if (attempt < 5)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            }
        }

        var authCode = store.AuthCodes[Phone];
        Assert.Equal(5, authCode.FailedAttempts);
        Assert.NotNull(authCode.LockedUntil);
        Assert.True(authCode.LockedUntil > _nowUtc.UtcDateTime);
    }

    [Fact]
    public async Task Confirm_CorrectCode_CreatesAccountAndClientWithoutDuplicates()
    {
        // Регистрации отдельно нет: аккаунт создаётся подтверждением заявки
        var store = new AuthStore();
        store.PendingOrders.Add(RepairOrder.CreatePending(
            Guid.NewGuid(), Phone, _nowUtc.UtcDateTime.AddDays(1)));
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Phone);

        var response = await Confirm(client, Phone, store.AuthCodes[Phone].Code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        Assert.NotNull(auth);
        Assert.NotEqual(Guid.Empty, auth.AccountId);
        Assert.Equal(auth.ClientId, store.AddedClients.Single().Id);
        Assert.Equal(auth.AccountId, store.AddedAccounts.Single().Id);
        // Новый аккаунт - клиент: панель мастера ему недоступна
        Assert.Equal(AccountRole.Client, auth.Role);

        // Токены: access не пуст, refresh-cookie установлена
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Confirm_Twice_ReusesSameAccountAndClient()
    {
        var store = new AuthStore();
        store.PendingOrders.Add(RepairOrder.CreatePending(
            Guid.NewGuid(), Phone, _nowUtc.UtcDateTime.AddDays(1)));
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Phone);
        var first = await Confirm(client, Phone, store.AuthCodes[Phone].Code);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstAuth = await first.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

        // Повторное подтверждение: новый код на тот же phone
        await RequestCode(client, Phone);
        var second = await Confirm(client, Phone, store.AuthCodes[Phone].Code);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondAuth = await second.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

        Assert.Equal(firstAuth!.AccountId, secondAuth!.AccountId);
        Assert.Equal(firstAuth.ClientId, secondAuth.ClientId);
        Assert.Single(store.AddedClients);
        Assert.Single(store.AddedAccounts);
    }

    [Fact]
    public async Task Confirm_NoAccountAndNoPendingOrder_Returns400()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Phone);

        var response = await Confirm(client, Phone, store.AuthCodes[Phone].Code);

        // Вход без заявки не регистрирует: аккаунт и клиент не создаются
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(store.AddedClients);
        Assert.Empty(store.AddedAccounts);
        Assert.True(store.AuthCodes.ContainsKey(Phone));
    }

    [Fact]
    public async Task Confirm_ExistingAccount_ReusesAccountWithoutDuplicates()
    {
        var existingClient = new Client { Id = Guid.NewGuid(), Name = "Иван", Phone = Phone };
        var existingAccount = new Account { Id = Guid.NewGuid(), ClientId = existingClient.Id };
        var store = new AuthStore { Clients = [existingClient], Accounts = [existingAccount] };
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Phone);

        var response = await Confirm(client, Phone, store.AuthCodes[Phone].Code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        Assert.Equal(existingAccount.Id, auth!.AccountId);
        Assert.Equal(existingClient.Id, auth.ClientId);
        Assert.Empty(store.AddedClients);
        Assert.Empty(store.AddedAccounts);
    }

    [Fact]
    public async Task Confirm_ClientWithoutAccountWithPendingOrder_CreatesAccountOnly()
    {
        var existingClient = new Client { Id = Guid.NewGuid(), Name = "Иван", Phone = Phone };
        var store = new AuthStore { Clients = [existingClient] };
        store.PendingOrders.Add(RepairOrder.CreatePending(
            Guid.NewGuid(), Phone, _nowUtc.UtcDateTime.AddDays(1)));
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Phone);

        var response = await Confirm(client, Phone, store.AuthCodes[Phone].Code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        Assert.Equal(existingClient.Id, auth!.ClientId);
        Assert.Empty(store.AddedClients);
        Assert.Single(store.AddedAccounts);
    }

    [Fact]
    public async Task Confirm_AttachesPendingOrderAndActivatesIt()
    {
        var store = new AuthStore();
        var pending = RepairOrder.CreatePending(
            Guid.NewGuid(),
            Phone,
            _nowUtc.UtcDateTime.AddDays(1),
            "Не заряжается");
        pending.ContactName = "Иван";
        store.PendingOrders.Add(pending);
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Phone);

        var response = await Confirm(client, Phone, store.AuthCodes[Phone].Code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        Assert.Equal("Иван", Assert.Single(store.AddedClients).Name);
        Assert.Equal("+79001234567", store.AddedClients[0].Phone);
        Assert.Equal(auth!.ClientId, pending.ClientId);
        Assert.Equal(RepairStatus.Created, pending.Status);
        Assert.Contains(pending.StatusHistory, h => h.Status == RepairStatus.Created);
    }

    [Fact]
    public async Task Confirm_MasterPhone_IssuesMasterRoleForSeededAccount()
    {
        var masterClient = new Client
        {
            Id = Guid.NewGuid(),
            Name = "Мастер",
            Phone = "+79001234567",
        };
        var masterAccount = new Account
        {
            Id = Guid.NewGuid(),
            ClientId = masterClient.Id,
            Role = AccountRole.Master,
        };
        var store = new AuthStore { Clients = [masterClient], Accounts = [masterAccount] };
        using var client = store.CreateFactory().CreateClient();

        await RequestCode(client, masterClient.Phone);
        var response = await Confirm(client, masterClient.Phone, store.AuthCodes[masterClient.Phone].Code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        Assert.Equal(AccountRole.Master, auth!.Role);
        Assert.Equal(masterAccount.Id, auth.AccountId);
        Assert.Equal(masterClient.Id, auth.ClientId);
        Assert.Empty(store.AddedAccounts);
        Assert.Empty(store.AddedClients);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        Assert.Equal("Master", Assert.Single(token.Claims, c => c.Type == AuthClaims.Role).Value);
    }

    [Fact]
    public async Task Confirm_MasterWithDifferentTrustedVkId_IsRejected()
    {
        var master = new Client { Id = Guid.NewGuid(), Name = "Мастер", Phone = Phone };
        var store = new AuthStore
        {
            Clients = [master],
            Accounts = [new Account { Id = Guid.NewGuid(), ClientId = master.Id, Role = AccountRole.Master }],
            TrustedMasterVkId = 43,
        };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient();
        await RequestCode(client, Phone);

        var response = await Confirm(client, Phone, store.AuthCodes[Phone].Code);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Profile_AfterConfirmationShowsVkAndDoesNotExposeEmail()
    {
        var existing = new Client { Id = Guid.NewGuid(), Name = "Иван", Phone = Phone };
        var store = new AuthStore
        {
            Clients = [existing],
            Accounts = [new Account { Id = Guid.NewGuid(), ClientId = existing.Id }],
        };
        using var factory = store.CreateFactory().WithWebHostBuilder(host => host.ConfigureTestServices(services =>
        {
            // Время JWT проверяется относительно тех же фиксированных часов, что и выдача токена
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                    expires > _nowUtc.UtcDateTime && (notBefore is null || notBefore <= _nowUtc.UtcDateTime));
        }));
        using var client = factory.CreateClient();
        await RequestCode(client, Phone);
        var confirmed = await Confirm(client, Phone, store.AuthCodes[Phone].Code);
        var auth = await confirmed.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth!.AccessToken);

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Phone, body.RootElement.GetProperty("phone").GetString());
        Assert.True(body.RootElement.GetProperty("vkLinked").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("email", out _));
    }
}
