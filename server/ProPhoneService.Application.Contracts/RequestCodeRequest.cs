using System.ComponentModel.DataAnnotations;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Запрос одноразового кода через привязку VK
/// </summary>
public record RequestCodeRequest
{
    /// <summary>
    /// Телефон, связанный с получателем VK
    /// </summary>
    [Required(ErrorMessage = "Укажите телефон")]
    public string Phone { get; init; } = "";
}
