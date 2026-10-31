using System.ComponentModel.DataAnnotations;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Запрос одноразового кода на email (единственный канал подтверждения в этом спринте)
/// </summary>
public record RequestCodeRequest
{
    /// <summary>
    /// Email для получения кода
    /// </summary>
    [Required(ErrorMessage = "Укажите email")]
    public string Email { get; init; } = "";
}
