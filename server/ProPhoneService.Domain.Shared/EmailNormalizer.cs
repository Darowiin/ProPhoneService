using System.ComponentModel.DataAnnotations;

namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Нормализация email для входа по коду и заявок: trim, нижний регистр, проверка формата
/// </summary>
public static class EmailNormalizer
{
    private static readonly EmailAddressAttribute _validator = new();

    /// <summary>
    /// Привести email к каноническому виду
    /// </summary>
    /// <param name="email">Введённый email (может содержать пробелы и верхний регистр)</param>
    /// <param name="normalized">Канонический email (пусто при неудаче)</param>
    /// <returns><c>true</c>, если email корректен</returns>
    public static bool TryNormalize(string? email, out string normalized)
    {
        var candidate = email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(candidate) || !_validator.IsValid(candidate))
        {
            normalized = "";
            return false;
        }

        normalized = candidate;
        return true;
    }
}
