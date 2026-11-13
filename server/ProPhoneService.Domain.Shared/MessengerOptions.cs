using System.Text.RegularExpressions;
using SystemEnum = System.Enum;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Настройки мессенджеров, секреты задаются через окружение
/// </summary>
public sealed class MessengerOptions
{
    /// <summary>
    /// Настройки Telegram
    /// </summary>
    public TelegramOptions Telegram { get; set; } = new();

    /// <summary>
    /// Настройки VK
    /// </summary>
    public VkOptions Vk { get; set; } = new();

    /// <summary>
    /// Проверить доступность настроек выбранного канала
    /// </summary>
    public bool IsConfigured(MessengerChannel channel) => channel switch
    {
        MessengerChannel.Telegram => Telegram.IsConfigured,
        MessengerChannel.Vk => Vk.IsConfigured,
        _ => false,
    };
}

/// <summary>
/// Доступ к Telegram Bot API
/// </summary>
public sealed class TelegramOptions
{
    /// <summary>
    /// Токен бота из BotFather
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// Имя бота без символа @
    /// </summary>
    public string BotUsername { get; set; } = "";

    /// <summary>
    /// Секрет заголовка X-Telegram-Bot-Api-Secret-Token
    /// </summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>
    /// Режим получения событий Telegram
    /// </summary>
    public TelegramUpdateMode UpdateMode { get; set; } = TelegramUpdateMode.Webhook;

    /// <summary>
    /// Полнота и допустимость настроек
    /// </summary>
    public bool IsConfigured => Regex.IsMatch(Token, @"^\d+:[A-Za-z0-9_-]+$")
        && Regex.IsMatch(BotUsername, @"^[A-Za-z0-9_]{5,32}$")
        && SystemEnum.IsDefined(UpdateMode)
        && (UpdateMode == TelegramUpdateMode.Polling
            || Regex.IsMatch(WebhookSecret, @"^[A-Za-z0-9_-]{16,256}$"));
}

/// <summary>
/// Доступ к сообщениям сообщества VK
/// </summary>
public sealed class VkOptions
{
    /// <summary>
    /// Токен сообщества с правом messages
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// Положительный идентификатор сообщества
    /// </summary>
    public long GroupId { get; set; }

    /// <summary>
    /// Секрет Callback API
    /// </summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>
    /// Строка подтверждения Callback API
    /// </summary>
    public string ConfirmationCode { get; set; } = "";

    /// <summary>
    /// Версия VK API
    /// </summary>
    public string ApiVersion { get; set; } = "5.199";

    /// <summary>
    /// Полнота настроек
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token) && GroupId > 0
        && !string.IsNullOrWhiteSpace(WebhookSecret) && !string.IsNullOrWhiteSpace(ConfirmationCode)
        && Regex.IsMatch(ApiVersion, @"^\d+\.\d+$");
}
