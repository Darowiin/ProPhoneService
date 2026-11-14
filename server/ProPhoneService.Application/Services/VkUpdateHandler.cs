using System.Globalization;
using System.Text.Json;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Привязка отправителя личного сообщения сообщества VK
/// </summary>
public class VkUpdateHandler(MessengerBindingService bindings, INotifier notifier, MessengerOptions options, AuthService auth)
    : IVkUpdateHandler
{
    /// <summary>
    /// Обработать message_new только от пользователя в личном диалоге нужного сообщества
    /// </summary>
    public async Task HandleAsync(JsonElement update, CancellationToken cancellationToken = default)
    {
        if (update.ValueKind != JsonValueKind.Object
            || Text(update, "type") != "message_new"
            || !Number(update, "group_id", out var groupId) || groupId != options.Vk.GroupId
            || !update.TryGetProperty("object", out var body) || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !Number(message, "from_id", out var senderId) || !Number(message, "peer_id", out var peerId)
            || senderId <= 0 || senderId != peerId)
        {
            return;
        }

        if (!await bindings.BindAsync(MessengerChannel.Vk, Text(message, "ref"), senderId, cancellationToken))
        {
            return;
        }

        var binding = await bindings.GetLinkedBindingAsync(Text(message, "ref")!, cancellationToken);
        if (binding is null)
        {
            return;
        }

        await notifier.SendAsync(MessengerChannel.Vk, senderId.ToString(CultureInfo.InvariantCulture),
            NotificationMessages.MessengerLinked,
            cancellationToken);

        await auth.RequestCodeAsync(binding.Phone, cancellationToken, binding.OrderId);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Number(JsonElement element, string name, out long number)
    {
        number = 0;

        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out number);
    }
}
