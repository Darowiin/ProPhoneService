using ProPhoneService.Domain.Shared.Enum;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Устройство (техника), сданное в ремонт
/// </summary>
[Table("device")]
public class Device
{
    /// <summary>
    /// Уникальный идентификатор устройства
    /// </summary>
    [Key]
    [Column("id")]
    public required Guid Id { get; set; }

    /// <summary>
    /// Тип устройства
    /// </summary>
    [Column("type")]
    public required DeviceType Type { get; set; }

    /// <summary>
    /// Производитель устройства
    /// </summary>
    [Column("manufacturer")]
    public required string Manufacturer { get; set; }

    /// <summary>
    /// Модель устройства
    /// </summary>
    [Column("model")]
    public required string Model { get; set; }

    /// <summary>
    /// Серийный номер устройства (необязательно)
    /// </summary>
    [Column("serial_number")]
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Заказы на ремонт, связанные с устройством
    /// </summary>
    public List<RepairOrder> RepairOrders { get; set; } = [];
}
