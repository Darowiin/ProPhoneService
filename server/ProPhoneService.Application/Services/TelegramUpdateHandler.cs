using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Общая обработка Telegram-событий из webhook и long polling
/// </summary>
public class TelegramUpdateHandler(MessengerBindingService bindings, INotifier notifier,
    ILogger<TelegramUpdateHandler> logger) : ITelegramUpdateHandler
{
    private const string LinkedMessage = "Мессенджер успешно привязан!\n"
        + "Ваш аккаунт успешно привязан к профилю на сайте.\n"
        + "Теперь вы можете получать уведомления о состоянии ваших заявок через этот мессенджер.";

    /// <summary>
    /// Обработать команду привязки из личного чата с ботом
    /// </summary>
    public async Task HandleAsync(JsonElement update, CancellationToken cancellationToken = default)
    {
        if (update.ValueKind != JsonValueKind.Object
            || !update.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!message.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object
            || !chat.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
            || type.GetString() != "private"
            || !message.TryGetProperty("from", out var sender) || sender.ValueKind != JsonValueKind.Object
            || !TryGetId(chat, out var chatId) || !TryGetId(sender, out var senderId)
            || chatId != senderId || senderId <= 0)
        {
            return;
        }

        if (!message.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var text = value.GetString();

        if (text is null || !text.StartsWith("/start ", StringComparison.Ordinal))
        {
            return;
        }

        var linked = await bindings.BindAsync(MessengerChannel.Telegram, text[7..].Trim(), senderId, cancellationToken);

        if (!linked)
        {
            return;
        }

        await notifier.SendAsync(MessengerChannel.Telegram, chatId.ToString(CultureInfo.InvariantCulture),
            LinkedMessage, cancellationToken);

        logger.LogInformation("Telegram: привязка мессенджера подтверждена");
    }

    private static bool TryGetId(JsonElement element, out long id)
    {
        id = 0;

        return element.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out id);
    }
}
