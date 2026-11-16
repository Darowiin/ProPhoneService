namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Общие правила подтверждения контакта и привязки VK
/// </summary>
public static class ConfirmationRules
{
    /// <summary>
    /// Срок действия кода, ссылки привязки и подтверждения заявки в минутах
    /// </summary>
    public const int LifetimeMinutes = 5;
}
