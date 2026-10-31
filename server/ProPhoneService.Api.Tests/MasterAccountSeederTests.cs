using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

/// <summary>
/// Проверка работы мастер-аккаунтов - мастер-аккаунт заводится апсертом при старте по email из конфигурации
/// </summary>
public class MasterAccountSeederTests
{
    private class ClientStore
    {
        public List<Client> Clients { get; init; } = [];

        public int Saves { get; private set; }

        public Mock<IClientRepository> Repository { get; }

        public Mock<IAccountRepository> Accounts { get; }

        public MasterAccountSeeder Seeder { get; }

        public ClientStore(string? masterEmail, IEnumerable<Account>? accounts = null)
        {
            var accountList = (accounts ?? []).ToList();

            Repository = new Mock<IClientRepository>();
            Repository
                .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string email, CancellationToken _) =>
                    Clients.FirstOrDefault(c => c.Email == email));
            Repository
                .Setup(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
                .Callback((Client c, CancellationToken _) => Clients.Add(c))
                .Returns(Task.CompletedTask);
            Repository
                .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => Saves++)
                .ReturnsAsync(1);

            Accounts = new Mock<IAccountRepository>();
            Accounts
                .Setup(r => r.GetByClientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid clientId, CancellationToken _) =>
                    accountList.FirstOrDefault(a => a.ClientId == clientId));
            Accounts
                .Setup(r => r.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()))
                .Callback((Account a, CancellationToken _) => accountList.Add(a))
                .Returns(Task.CompletedTask);

            Seeder = new MasterAccountSeeder(
                Repository.Object,
                Accounts.Object,
                new AuthOptions { MasterEmail = masterEmail },
                NullLogger<MasterAccountSeeder>.Instance);

            AddedAccounts = accountList;
        }

        public List<Account> AddedAccounts { get; }
    }

    [Fact]
    public async Task Ensure_WithoutMasterEmail_DoesNothing()
    {
        var store = new ClientStore(null);

        var changed = await store.Seeder.EnsureAsync();

        Assert.False(changed);
        Assert.Empty(store.Clients);
        Assert.Empty(store.AddedAccounts);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Ensure_NewMasterEmail_CreatesClientAndMasterAccount()
    {
        var store = new ClientStore("  Boss@Example.COM ");

        var changed = await store.Seeder.EnsureAsync();

        Assert.True(changed);
        var client = Assert.Single(store.Clients);
        Assert.Equal("boss@example.com", client.Email);
        Assert.Equal("Мастер", client.Name);

        var account = Assert.Single(store.AddedAccounts);
        Assert.Equal(client.Id, account.ClientId);
        Assert.Equal(AccountRole.Master, account.Role);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Ensure_ExistingClientWithClientAccount_PromotesToMaster()
    {
        var client = new Client { Id = Guid.NewGuid(), Name = "Босс", Email = "boss@example.com" };
        var account = new Account { Id = Guid.NewGuid(), ClientId = client.Id, Role = AccountRole.Client };
        var store = new ClientStore("boss@example.com", [account]);
        store.Clients.Add(client);

        var changed = await store.Seeder.EnsureAsync();

        Assert.True(changed);
        Assert.Equal(AccountRole.Master, account.Role);
        Assert.Single(store.AddedAccounts);
        store.Accounts.Verify(a => a.Update(account), Times.Once);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Ensure_AlreadyMaster_IsIdempotent()
    {
        var client = new Client { Id = Guid.NewGuid(), Name = "Босс", Email = "boss@example.com" };
        var account = new Account { Id = Guid.NewGuid(), ClientId = client.Id, Role = AccountRole.Master };
        var store = new ClientStore("boss@example.com", [account]);
        store.Clients.Add(client);

        var changed = await store.Seeder.EnsureAsync();

        Assert.False(changed);
        Assert.Equal(0, store.Saves);
    }
}
