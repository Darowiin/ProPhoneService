using System.ComponentModel.DataAnnotations;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Запрос подтверждения email кодом
/// </summary>
public record ConfirmAuthRequest
{
    /// <summary>
    /// Email, на который был отправлен код
    /// </summary>
    [Required(ErrorMessage = "Укажите email")]
    public string Email { get; init; } = "";

    /// <summary>
    /// Одноразовый код (6 цифр)
    /// </summary>
    [Required(ErrorMessage = "Введите код")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Код должен состоять из 6 цифр")]
    public string Code { get; init; } = "";
}
