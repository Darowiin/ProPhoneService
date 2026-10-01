using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProPhoneService.Domain.Model;

/// <summary>
/// Клиент мастерской (автор заказов и отзывов)
/// </summary>
[Table("client")]
public class Client
{
    /// <summary>
    /// Уникальный идентификатор клиента
    /// </summary>
    [Key]
    [Column("id")]
    public required Guid Id { get; set; }

    /// <summary>
    /// Имя клиента
    /// </summary>
    [Column("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Телефон клиента (необязательно)
    /// </summary>
    [Column("phone")]
    public string? Phone { get; set; }

    /// <summary>
    /// Email клиента (необязательно)
    /// </summary>
    [Column("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Дата и время регистрации клиента (UTC)
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Заказы на ремонт, оформленные клиентом
    /// </summary>
    public List<RepairOrder> RepairOrders { get; set; } = [];
}
