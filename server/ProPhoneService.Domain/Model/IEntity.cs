namespace ProPhoneService.Domain.Model;

/// <summary>
/// Сущность с одиночным первичным ключом <c>Guid Id</c>
/// </summary>
public interface IEntity
{
    /// <summary>
    /// Уникальный идентификатор сущности
    /// </summary>
    Guid Id { get; }
}
