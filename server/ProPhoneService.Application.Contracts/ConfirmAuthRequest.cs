using System.ComponentModel.DataAnnotations;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Запрос подтверждения телефона кодом
/// </summary>
public record ConfirmAuthRequest
{
    /// <summary>
    /// Телефон, для которого отправлен код через VK
    /// </summary>
    [Required(ErrorMessage = "Укажите телефон")]
    public string Phone { get; init; } = "";

    /// <summary>
    /// Одноразовый код (6 цифр)
    /// </summary>
    [Required(ErrorMessage = "Введите код")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Код должен состоять из 6 цифр")]
    public string Code { get; init; } = "";
}
