namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Проверка телефона в едином формате +7XXXXXXXXXX
/// </summary>
public static class PhoneNormalizer
{
    /// <summary>
    /// Проверить и удалить пробелы по краям телефона
    /// </summary>
    public static bool TryNormalize(string? value, out string phone)
    {
        phone = value?.Trim() ?? "";

        return phone.Length == 12 && phone.StartsWith("+7", StringComparison.Ordinal)
            && phone[2..].All(char.IsAsciiDigit);
    }
}
