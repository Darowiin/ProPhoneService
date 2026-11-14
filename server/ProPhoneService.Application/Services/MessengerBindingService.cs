using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Создание одноразовой ссылки и привязка отправителя проверенного события бота
/// </summary>
public class MessengerBindingService(IMessengerBindingRepository bindings,
    IRepairOrderRepository orders, IAccountRepository accounts, IClientRepository clients,
    MessengerOptions options, TimeProvider timeProvider)
{
    /// <summary>
    /// Создать ссылку для неподтверждённой гостевой заявки
    /// </summary>
    public async Task<MessengerLinkResponse?> CreateForOrderAsync(Guid orderId, MessengerChannel channel,
        CancellationToken cancellationToken = default)
    {
        var order = await orders.GetByIdAsync(orderId, cancellationToken);

        if (order is null || order.Status != RepairStatus.PendingContact
            || order.CreatedAt.AddMinutes(5) <= timeProvider.GetUtcNow().UtcDateTime)
            return null;

        return await CreateAsync(order.ContactPhone, channel, order.Id, null,
            order.CreatedAt.AddMinutes(5), cancellationToken);
    }

    /// <summary>
    /// Создать ссылку для аккаунта текущего пользователя
    /// </summary>
    public async Task<MessengerLinkResponse?> CreateForAccountAsync(Guid accountId, MessengerChannel channel,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.GetByIdAsync(accountId, cancellationToken);
        var client = account is null ? null : await clients.GetByIdAsync(account.ClientId, cancellationToken);

        return client is null ? null : await CreateAsync(client.Phone, channel, null, accountId,
            timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5), cancellationToken);
    }

    private async Task<MessengerLinkResponse> CreateAsync(string? phone, MessengerChannel channel,
        Guid? orderId, Guid? accountId, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(channel))
            throw new ArgumentOutOfRangeException(nameof(channel));

        if (!options.IsConfigured(channel))
            throw new MessengerUnavailableException(channel);

        if (phone is null || phone.Length != 12 || !phone.StartsWith("+7") || !phone[2..].All(char.IsAsciiDigit))
            throw new ArgumentException("Укажите телефон в формате +7XXXXXXXXXX", nameof(phone));

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        await bindings.AddAsync(new MessengerBinding
        {
            Id = Guid.NewGuid(),
            TokenHash = Hash(token),
            Phone = phone,
            Channel = channel,
            OrderId = orderId,
            AccountId = accountId,
            ExpiresAt = expiresAtUtc,
        }, cancellationToken);
        await bindings.SaveChangesAsync(cancellationToken);

        var url = $"https://vk.me/club{options.Vk.GroupId.ToString(CultureInfo.InvariantCulture)}?ref={token}";

        return new MessengerLinkResponse(channel, url, expiresAtUtc);
    }

    /// <summary>
    /// Погасить ссылку только для ожидаемого канала и получателя из проверенного события VK
    /// </summary>
    public async Task<bool> BindAsync(MessengerChannel channel, string? token, long recipientId,
        CancellationToken cancellationToken = default)
    {
        if (token is null || token.Length != 64 || !token.All(char.IsAsciiHexDigit) || recipientId <= 0)
            return false;

        var hash = Hash(token);
        var binding = await bindings.GetByTokenHashAsync(hash, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (binding is null || binding.Channel != channel || binding.ExpiresAt <= now)
            return false;

        if (binding.OrderId is { } orderId)
        {
            var order = await orders.GetByIdAsync(orderId, cancellationToken);

            if (order is null || order.Status != RepairStatus.PendingContact)
                return false;
        }

        var recipient = recipientId.ToString(CultureInfo.InvariantCulture);

        // Повтор события от того же пользователя безопасен, другой пользователь токеном не завладеет
        if (binding.RecipientId is not null)
            return binding.RecipientId == recipient;

        return await bindings.TryBindAsync(hash, channel, recipient, now, cancellationToken);
    }

    /// <summary>
    /// Получить сохранённую привязку после успешной обработки секрета
    /// </summary>
    public Task<MessengerBinding?> GetLinkedBindingAsync(string token, CancellationToken cancellationToken = default) =>
        bindings.GetByTokenHashAsync(Hash(token), cancellationToken);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
