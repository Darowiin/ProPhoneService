using ProPhoneService.Domain.Shared.Enum;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Заказ на ремонт телефона в мастерской
/// </summary>
[Table("repair_order")]
public class RepairOrder
{
    /// <summary>
    /// Уникальный идентификатор заказа
    /// </summary>
    [Key]
    [Column("id")]
    public required Guid Id { get; set; }

    /// <summary>
    /// Идентификатор клиента, оформившего заказ
    /// </summary>
    [ForeignKey(nameof(ClientId))]
    [Column("client_id")]
    public required Guid ClientId { get; set; }

    /// <summary>
    /// Идентификатор устройства в ремонте
    /// </summary>
    [ForeignKey(nameof(DeviceId))]
    [Column("device_id")]
    public required Guid DeviceId { get; set; }

    /// <summary>
    /// Текущий статус заказа
    /// </summary>
    [Column("status")]
    public required RepairStatus Status { get; set; }

    /// <summary>
    /// Итоговая стоимость ремонта, руб
    /// </summary>
    [Column("total_price", TypeName = "decimal(10,2)")]
    public decimal TotalPrice { get; set; }

    /// <summary>
    /// Дата и время создания заказа (UTC)
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Клиент, оформивший заказ
    /// </summary>
    public Client? Client { get; set; }

    /// <summary>
    /// Устройство в ремонте
    /// </summary>
    public Device? Device { get; set; }

    /// <summary>
    /// История смены статусов заказа
    /// </summary>
    public List<RepairStatusHistory> StatusHistory { get; set; } = [];

    /// <summary>
    /// Услуги, входящие в заказ
    /// </summary>
    public List<RepairOrderService> Services { get; set; } = [];
}
