using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Одноразовый код подтверждения телефона для входа (доставляется через VK)
/// </summary>
[Table("auth_code")]
public class AuthCode : IEntity
{
    /// <summary>
    /// Уникальный идентификатор записи
    /// </summary>
    [Key]
    [Column("id")]
    public required Guid Id { get; set; }

    /// <summary>
    /// Телефон — один активный код на контакт
    /// </summary>
    [Column("phone")]
    public required string Phone { get; set; }

    /// <summary>
    /// Привязка получателя VK, которому доставлен код
    /// </summary>
    [Column("binding_id")]
    public Guid BindingId { get; set; }

    /// <summary>
    /// Код подтверждения (6 цифр)
    /// </summary>
    [Column("code")]
    public required string Code { get; set; }

    /// <summary>
    /// Момент, после которого код недействителен (UTC)
    /// </summary>
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Количество неверных попыток ввода кода
    /// </summary>
    [Column("failed_attempts")]
    public int FailedAttempts { get; set; }

    /// <summary>
    /// Момент окончания блокировки контакта после превышения лимита (UTC) или <c>null</c>
    /// </summary>
    [Column("locked_until")]
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// Дата и время последнего запроса кода (UTC)
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
