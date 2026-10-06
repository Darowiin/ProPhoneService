using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Услуга из прайс-листа мастерской
/// </summary>
[Table("service")]
public class Service : IEntity
{
    /// <summary>
    /// Уникальный идентификатор услуги
    /// </summary>
    [Key]
    [Column("id")]
    public required Guid Id { get; set; }

    /// <summary>
    /// Название услуги
    /// </summary>
    [Column("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Описание услуги (необязательно)
    /// </summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Стоимость услуги в прайс-листе, руб
    /// </summary>
    [Column("price", TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    /// <summary>
    /// Признак активности услуги в прайс-листе
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Заказы, в которые входит услуга
    /// </summary>
    public List<RepairOrderService> RepairOrders { get; set; } = [];
}
