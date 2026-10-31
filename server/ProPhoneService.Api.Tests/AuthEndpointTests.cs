using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;

namespace ProPhoneService.Api.Tests;

public class AuthEndpointTests
{
    /// <summary>
    /// Понедельник 2026-10-05 08:00 UTC = 12:00 по Самаре
    /// </summary>
    private static readonly DateTimeOffset _nowUtc = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private const string Email = "ivan@example.com";

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => _nowUtc;
    }

    /// <summary>
    /// Mock-хранилища auth-сущностей + перехват отправителя сообщений
    /// </summary>
    private class AuthStore
    {
        public List<Client> Clients { get; set; } = [];

        public List<Account> Accounts { get; set; } = [];

        // Словарь имитирует хранение по email (один активный код на адрес)
        public Dictionary<string, AuthCode> AuthCodes { get; set; } = [];

        public List<(string Target, string Text)> Notifications { get; } = [];

        public List<Client> AddedClients { get; } = [];

        public List<Account> AddedAccounts { get; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.ConfigureTestServices(services =>
                {
                    var authCodeRepository = new Mock<IAuthCodeRepository>();
                    authCodeRepository
                        .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string email, CancellationToken _) => AuthCodes.GetValueOrDefault(email));

                    authCodeRepository
                        .Setup(r => r.AddAsync(It.IsAny<AuthCode>(), It.IsAny<CancellationToken>()))
                        .Callback((AuthCode code, CancellationToken _) => AuthCodes[code.Email] = code)
                        .Returns(Task.CompletedTask);

                    authCodeRepository
                        .Setup(r => r.Update(It.IsAny<AuthCode>()))
                        .Callback((AuthCode code) => AuthCodes[code.Email] = code);

                    authCodeRepository
                        .Setup(r => r.Remove(It.IsAny<AuthCode>()))
                        .Callback((AuthCode code) => AuthCodes.Remove(code.Email));

                    authCodeRepository
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    var clientRepository = new Mock<IClientRepository>();
                    clientRepository
                        .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string email, CancellationToken _) =>
                            Clients.FirstOrDefault(c => c.Email == email));

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

                    accountRepository
                        .Setup(r => r.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()))
                        .Callback((Account a, CancellationToken _) =>
                        {
                            Accounts.Add(a);
                            AddedAccounts.Add(a);
                        })
                        .Returns(Task.CompletedTask);

                    var notifier = new Mock<INotifier>();
                    notifier
                        .Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Callback((string target, string text, CancellationToken _) => Notifications.Add((target, text)))
                        .Returns(Task.CompletedTask);

                    services.RemoveAll<IAuthCodeRepository>();
                    services.AddScoped(_ => authCodeRepository.Object);
                    services.RemoveAll<IClientRepository>();
                    services.AddScoped(_ => clientRepository.Object);
                    services.RemoveAll<IAccountRepository>();
                    services.AddScoped(_ => accountRepository.Object);
                    services.RemoveAll<INotifier>();
                    services.AddScoped(_ => notifier.Object);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider());
                });
            });
        }
    }

    private static Task<HttpResponseMessage> RequestCode(HttpClient client, string email)
    {
        return client.PostAsJsonAsync("/api/auth/request-code", new { email });
    }

    private static Task<HttpResponseMessage> Confirm(HttpClient client, string email, string code)
    {
        return client.PostAsJsonAsync("/api/auth/confirm", new { email, code });
    }

    [Fact]
    public async Task RequestCode_ValidEmail_SendsCodeThroughNotifier()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();

        // Email нормализуется (trim + нижний регистр)
        var response = await RequestCode(client, "  Ivan@Example.COM ");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var sent = Assert.Single(store.Notifications);
        Assert.Equal(Email, sent.Target);

        // Код - 6 цифр и совпадает с сохранённым
        var match = Regex.Match(sent.Text, @"\b\d{6}\b");
        Assert.True(match.Success);
        var savedCode = store.AuthCodes[Email];
        Assert.Equal(match.Value, savedCode.Code);
        Assert.Equal(_nowUtc.UtcDateTime.AddMinutes(5), savedCode.ExpiresAt);
    }

    [Fact]
    public async Task RequestCode_PhoneIsNotAccepted_Returns400()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();

        var response = await RequestCode(client, "+79001234567");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(store.Notifications);
    }

    [Fact]
    public async Task RequestCode_InvalidEmail_Returns400()
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

        await RequestCode(client, Email);
        var second = await RequestCode(client, Email);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Single(store.Notifications);
    }

    [Fact]
    public async Task Confirm_UnknownEmail_Returns400()
    {
        using var client = new AuthStore().CreateFactory().CreateClient();

        var response = await Confirm(client, Email, "123456");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_WrongCodeFiveTimes_LocksEmail()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Email);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await Confirm(client, Email, "000000");

            if (attempt < 5)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            }
        }

        var authCode = store.AuthCodes[Email];
        Assert.Equal(5, authCode.FailedAttempts);
        Assert.NotNull(authCode.LockedUntil);
        Assert.True(authCode.LockedUntil > _nowUtc.UtcDateTime);
    }

    [Fact]
    public async Task Confirm_CorrectCode_CreatesAccountAndClientWithoutDuplicates()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Email);

        var response = await Confirm(client, Email, store.AuthCodes[Email].Code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.NotEqual(Guid.Empty, auth.AccountId);
        Assert.Equal(auth.ClientId, store.AddedClients.Single().Id);
        Assert.Equal(auth.AccountId, store.AddedAccounts.Single().Id);

        // Токены: access не пуст, refresh-cookie установлена
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.True(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Confirm_Twice_ReusesSameAccountAndClient()
    {
        var store = new AuthStore();
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Email);
        var first = await Confirm(client, Email, store.AuthCodes[Email].Code);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstAuth = await first.Content.ReadFromJsonAsync<AuthResponse>();

        // Повторное подтверждение: новый код на тот же email
        await RequestCode(client, Email);
        var second = await Confirm(client, Email, store.AuthCodes[Email].Code);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondAuth = await second.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.Equal(firstAuth!.AccountId, secondAuth!.AccountId);
        Assert.Equal(firstAuth.ClientId, secondAuth.ClientId);
        Assert.Single(store.AddedClients);
        Assert.Single(store.AddedAccounts);
    }

    [Fact]
    public async Task Confirm_ExistingEmail_ReusesClientWithoutDuplicate()
    {
        var existing = new Client
        {
            Id = Guid.NewGuid(),
            Name = "Иван",
            Email = Email,
        };
        var store = new AuthStore { Clients = [existing] };
        using var client = store.CreateFactory().CreateClient();
        await RequestCode(client, Email);

        var response = await Confirm(client, Email, store.AuthCodes[Email].Code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(existing.Id, auth!.ClientId);
        Assert.Empty(store.AddedClients);
        Assert.Single(store.AddedAccounts);
    }
}
