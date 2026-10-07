using ProPhoneService.Domain.Model;

namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Универсальный репозиторий: CRUD-операции над сущностью с одиночным ключом <c>Guid Id</c>
/// </summary>
/// <typeparam name="T">Тип сущности, реализующей <see cref="IEntity"/></typeparam>
public interface IRepository<T> where T : class, IEntity
{
    /// <summary>
    /// Получить сущность по идентификатору или <c>null</c>, если не найдена
    /// </summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить страницу сущностей (пагинация Skip/Take)
    /// </summary>
    /// <param name="skip">Количество пропускаемых записей</param>
    /// <param name="take">Количество возвращаемых записей</param>
    Task<IReadOnlyList<T>> GetAllAsync(int skip = 0, int take = 25, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавить новую сущность (сохранение — через <see cref="SaveChangesAsync"/>)
    /// </summary>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Пометить сущность как изменённую
    /// </summary>
    void Update(T entity);

    /// <summary>
    /// Пометить сущность как удалённую
    /// </summary>
    void Remove(T entity);

    /// <summary>
    /// Сохранить накопленные изменения одним батчем
    /// </summary>
    /// <returns>Количество затронутых записей</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
