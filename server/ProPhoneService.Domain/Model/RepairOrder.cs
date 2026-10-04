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
    public RepairStatus Status { get; private set; }

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

    /// <summary>
    /// Установить начальный статус заказа (только один раз, при создании)
    /// </summary>
    /// <exception cref="InvalidOperationException">Статус уже установлен</exception>
    public void Start()
    {
        if (StatusHistory.Count != 0)
        {
            throw new InvalidOperationException("Начальный статус заказа уже установлен");
        }

        Status = RepairStatus.Created;
        StatusHistory.Add(new RepairStatusHistory
        {
            Id = Guid.NewGuid(),
            RepairOrderId = Id,
            Status = RepairStatus.Created,
        });
    }

    /// <summary>
    /// Сменить статус заказа по правилам машины состояний
    /// </summary>
    /// <param name="next">Целевой статус</param>
    /// <param name="comment">Комментарий к смене статуса</param>
    /// <exception cref="InvalidOperationException">Переход из текущего статуса в целевой запрещён</exception>
    public void ChangeStatus(RepairStatus next, string? comment = null)
    {
        if (!RepairStatusTransitions.IsAllowed(Status, next))
        {
            throw new InvalidOperationException(
                $"Недопустимый переход статуса заказа: {Status} -> {next}");
        }

        Status = next;
        StatusHistory.Add(new RepairStatusHistory
        {
            Id = Guid.NewGuid(),
            RepairOrderId = Id,
            Status = next,
            Comment = comment,
        });
    }
}
