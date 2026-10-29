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
    [Range(0, double.MaxValue, ErrorMessage = "Цена не может быть отрицательной")]
    public decimal Price { get; init; }
}
