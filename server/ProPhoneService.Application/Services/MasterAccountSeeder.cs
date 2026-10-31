using Microsoft.Extensions.Logging;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Апсерт мастер-аккаунта при запуске: email берётся из настройки <see cref="AuthOptions.MasterEmail"/>,
/// клиент и аккаунт создаются при отсутствии, существующему аккаунту проставляется роль Master
/// </summary>
public class MasterAccountSeeder(
    IClientRepository clientRepository,
    IAccountRepository accountRepository,
    AuthOptions authOptions,
    ILogger<MasterAccountSeeder> logger)
{
    /// <summary>
    /// Экранит мастер-аккаунт для <see cref="AuthOptions.MasterEmail"/>; без настройки ничего не делает
    /// </summary>
    /// <returns><c>true</c>, если данные были созданы или изменены</returns>
    public async Task<bool> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var email = authOptions.MasterEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var client = await clientRepository.GetByEmailAsync(email, cancellationToken);
        if (client is null)
        {
            client = new Client
            {
                Id = Guid.NewGuid(),
                Name = "Мастер",
                Email = email,
            };
            await clientRepository.AddAsync(client, cancellationToken);
        }

        var account = await accountRepository.GetByClientIdAsync(client.Id, cancellationToken);
        if (account is null)
        {
            await accountRepository.AddAsync(new Account
            {
                Id = Guid.NewGuid(),
                ClientId = client.Id,
                Role = AccountRole.Master,
            }, cancellationToken);
        }
        else if (account.Role != AccountRole.Master)
        {
            account.Role = AccountRole.Master;
            accountRepository.Update(account);
        }
        else
        {
            return false;
        }

        await clientRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Мастер-аккаунт готов: {Email}", email);

        return true;
    }
}
