using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Лог отправки уведомлений клиенту: кому, когда, по какому заказу и чем закончилось
/// </summary>
[Table("notification_log")]
public class NotificationLog : IEntity
{
    /// <summary>
    /// Уникальный идентификатор записи
    /// </summary>
    [Key]
    [Column("id")]
    public required Guid Id { get; set; }

    /// <summary>
    /// Заказ, по которому отправлено уведомление
    /// </summary>
    [ForeignKey(nameof(RepairOrderId))]
    [Column("repair_order_id")]
    public required Guid RepairOrderId { get; set; }

    /// <summary>
    /// Телефон получателя
    /// </summary>
    [Column("recipient")]
    public string? Recipient { get; set; }

    /// <summary>
    /// Тема сообщения
    /// </summary>
    [Column("subject")]
    public required string Subject { get; set; }

    /// <summary>
    /// Текст сообщения
    /// </summary>
    [Column("message")]
    public required string Message { get; set; }

    /// <summary>
    /// Статус доставки
    /// </summary>
    [Column("status")]
    public required NotificationDeliveryStatus Status { get; set; }

    /// <summary>
    /// Количество выполненных попыток отправки
    /// </summary>
    [Column("attempts")]
    public required int Attempts { get; set; }

    /// <summary>
    /// Момент отправки (UTC, штампует AuditInterceptor)
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Заказ, по которому отправлено уведомление
    /// </summary>
    public RepairOrder? RepairOrder { get; set; }
}
