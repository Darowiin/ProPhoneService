using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Globalization;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Апсерт мастер-аккаунта при запуске: телефон берётся из настройки <see cref="AuthOptions.MasterPhone"/>,
/// клиент и аккаунт создаются при отсутствии, существующему аккаунту проставляется роль Master
/// </summary>
public class MasterAccountSeeder(
    IClientRepository clientRepository,
    IAccountRepository accountRepository,
    IMessengerBindingRepository bindings,
    AuthOptions authOptions,
    ILogger<MasterAccountSeeder> logger)
{
    /// <summary>
    /// Подготовить мастер-аккаунт для <see cref="AuthOptions.MasterPhone"/>; без настройки ничего не делает
    /// </summary>
    /// <returns><c>true</c>, если данные были созданы или изменены</returns>
    public async Task<bool> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var phone = authOptions.MasterPhone?.Trim();
        if (!PhoneNormalizer.TryNormalize(phone, out phone) || authOptions.MasterVkUserId <= 0)
        {
            return false;
        }

        var changed = false;
        var client = await clientRepository.GetByPhoneAsync(phone, cancellationToken);
        if (client is null)
        {
            client = new Client
            {
                Id = Guid.NewGuid(),
                Name = "Мастер",
                Phone = phone,
            };
            await clientRepository.AddAsync(client, cancellationToken);
            changed = true;
        }

        var account = await accountRepository.GetByClientIdAsync(client.Id, cancellationToken);
        if (account is null)
        {
            account = new Account
            {
                Id = Guid.NewGuid(),
                ClientId = client.Id,
                Role = AccountRole.Master,
            };
            await accountRepository.AddAsync(account, cancellationToken);
            changed = true;
        }
        else if (account.Role != AccountRole.Master)
        {
            account.Role = AccountRole.Master;
            accountRepository.Update(account);
            changed = true;
        }

        var binding = await bindings.GetByPhoneAsync(phone, cancellationToken);
        var recipient = authOptions.MasterVkUserId.ToString(CultureInfo.InvariantCulture);
        if (binding is not null && binding.RecipientId != recipient)
        {
            throw new InvalidOperationException("VK мастер-аккаунта не совпадает с доверенным ID в конфигурации");
        }

        if (binding is null)
        {
            changed = true;
            await bindings.AddAsync(new MessengerBinding
            {
                Id = Guid.NewGuid(),
                TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Channel = MessengerChannel.Vk,
                Phone = phone,
                AccountId = account.Id,
                RecipientId = recipient,
                LinkedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow,
            }, cancellationToken);
        }

        if (!changed)
        {
            return false;
        }

        await clientRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Мастер-аккаунт готов: {AccountId}", account.Id);

        return true;
    }
}
