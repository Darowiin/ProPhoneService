using System.ComponentModel.DataAnnotations;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Данные услуги прайс-листа при создании и правке (Для мастера)
/// </summary>
public record ServiceUpsertRequest
{
    /// <summary>
    /// Название услуги
    /// </summary>
    [Required(ErrorMessage = "Укажите название услуги")]
    [StringLength(200, ErrorMessage = "Название услуги слишком длинное")]
    public string Name { get; init; } = "";

    /// <summary>
    /// Описание услуги (необязательно)
    /// </summary>
    [StringLength(2000, ErrorMessage = "Описание услуги слишком длинное")]
    public string? Description { get; init; }

    /// <summary>
    /// Стоимость услуги, руб (неотрицательная)
    /// </summary>
    [Range(typeof(decimal), "0", "99999999.99", ParseLimitsInInvariantCulture = true, ErrorMessage = "Цена должна быть от 0 до 99999999.99")]
    public decimal Price { get; init; }
}
