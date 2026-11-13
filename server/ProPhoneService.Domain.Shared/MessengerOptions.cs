using System.Text.RegularExpressions;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Настройки мессенджеров, секреты задаются через окружение
/// </summary>
public sealed class MessengerOptions
{
    /// <summary>
    /// Настройки VK
    /// </summary>
    public VkOptions Vk { get; set; } = new();

    /// <summary>
    /// Проверить доступность настроек выбранного канала
    /// </summary>
    public bool IsConfigured(MessengerChannel channel) => channel switch
    {
        MessengerChannel.Vk => Vk.IsConfigured,
        _ => false,
    };
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
    /// Версия VK API
    /// </summary>
    public string ApiVersion { get; set; } = "5.199";

    /// <summary>
    /// Полнота настроек
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token) && GroupId > 0
        && Regex.IsMatch(ApiVersion, @"^\d+\.\d+$");
}
