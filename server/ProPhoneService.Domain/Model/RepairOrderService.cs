using System.ComponentModel.DataAnnotations.Schema;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Связь заказ — услуга: услуги, входящие в заказ на ремонт, с фиксированной ценой
/// </summary>
[Table("repair_order_service")]
public class RepairOrderService
{
    /// <summary>
    /// Идентификатор заказа (часть составного первичного ключа)
    /// </summary>
    [ForeignKey(nameof(RepairOrderId))]
    [Column("repair_order_id")]
    public required Guid RepairOrderId { get; set; }

    /// <summary>
    /// Идентификатор услуги (часть составного первичного ключа)
    /// </summary>
    [ForeignKey(nameof(ServiceId))]
    [Column("service_id")]
    public required Guid ServiceId { get; set; }

    /// <summary>
    /// Стоимость услуги в рамках конкретного заказа, руб
    /// </summary>
    [Column("price", TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    /// <summary>
    /// Заказ, в который входит услуга
    /// </summary>
    public RepairOrder? RepairOrder { get; set; }

    /// <summary>
    /// Услуга, включённая в заказ
    /// </summary>
    public Service? Service { get; set; }
}
