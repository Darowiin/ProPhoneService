using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Выбор отправителя по каналу, выбранному клиентом
/// </summary>
public class MessengerNotifier(TelegramBotNotifier telegram, VkNotifier vk,
    IMessengerBindingRepository bindings) : INotifier
{
    /// <inheritdoc />
    public Task SendAsync(MessengerChannel channel, string recipientId, string text,
        CancellationToken cancellationToken = default) => channel switch
    {
        MessengerChannel.Telegram => telegram.SendAsync(recipientId, text, cancellationToken),
        MessengerChannel.Vk => vk.SendAsync(recipientId, text, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    /// <inheritdoc />
    public async Task SendAsync(string target, string subject, string text, CancellationToken cancellationToken = default)
    {
        if (target.Length != 12 || !target.StartsWith("+7") || !target[2..].All(char.IsAsciiDigit))
            throw new MessengerUnavailableException();

        var binding = await bindings.GetByPhoneAsync(target, cancellationToken)
            ?? throw new MessengerUnavailableException();

        await SendAsync(binding.Channel, binding.RecipientId!, text, cancellationToken);
    }
}
