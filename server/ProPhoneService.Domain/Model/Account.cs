using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Аккаунт пользователя: связка 1:1 на клиента; вход без пароля - по коду на контакт клиента
/// </summary>
[Table("account")]
public class Account : IEntity
{
    /// <summary>
    /// Уникальный идентификатор аккаунта
    /// </summary>
    [Key]
    [Column("id")]
    public required Guid Id { get; set; }

    /// <summary>
    /// Идентификатор клиента (один аккаунт на одного клиента)
    /// </summary>
    [ForeignKey(nameof(ClientId))]
    [Column("client_id")]
    public required Guid ClientId { get; set; }

    /// <summary>
    /// Роль аккаунта: клиент или мастер (по умолчанию - клиент)
    /// </summary>
    [Column("role")]
    public AccountRole Role { get; set; } = AccountRole.Client;

    /// <summary>
    /// Дата и время создания аккаунта (UTC)
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Клиент аккаунта
    /// </summary>
    public Client? Client { get; set; }
}
