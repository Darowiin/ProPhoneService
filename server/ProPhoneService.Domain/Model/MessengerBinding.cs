using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Одноразовая ссылка и подтверждённая привязка получателя к заявке или аккаунту
/// </summary>
[Table("messenger_binding")]
public class MessengerBinding : IEntity
{
    /// <summary>
    /// Идентификатор привязки
    /// </summary>
    [Key]
    [Column("id")]
    public required Guid Id { get; set; }

    /// <summary>
    /// SHA256 одноразового токена из ссылки
    /// </summary>
    [Column("token_hash")]
    public required string TokenHash { get; set; }

    /// <summary>
    /// Выбранный мессенджер
    /// </summary>
    [Column("channel")]
    public MessengerChannel Channel { get; set; }

    /// <summary>
    /// Телефон из заявки или профиля
    /// </summary>
    [Column("phone")]
    public required string Phone { get; set; }

    /// <summary>
    /// Заявка для гостевой привязки
    /// </summary>
    [Column("order_id")]
    public Guid? OrderId { get; set; }

    /// <summary>
    /// Аккаунт для привязки из личного кабинета
    /// </summary>
    [Column("account_id")]
    public Guid? AccountId { get; set; }

    /// <summary>
    /// Предельное время использования ссылки в UTC
    /// </summary>
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Получатель из проверенного события мессенджера
    /// </summary>
    [Column("recipient_id")]
    public string? RecipientId { get; set; }

    /// <summary>
    /// Время успешной привязки в UTC
    /// </summary>
    [Column("linked_at")]
    public DateTime? LinkedAt { get; set; }
}
