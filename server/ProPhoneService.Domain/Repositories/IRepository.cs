namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Универсальный репозиторий: CRUD-операции над сущностью
/// </summary>
/// <typeparam name="T">Тип сущности с ключом <c>Guid Id</c></typeparam>
public interface IRepository<T> where T : class
{
    /// <summary>
    /// Получить сущность по идентификатору или <c>null</c>, если не найдена
    /// </summary>
    public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить страницу сущностей (пагинация Skip/Take)
    /// </summary>
    /// <param name="skip">Количество пропускаемых записей</param>
    /// <param name="take">Количество возвращаемых записей</param>
    public Task<IReadOnlyList<T>> GetAllAsync(int skip = 0, int take = 25, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавить новую сущность (сохранение — через <see cref="SaveChangesAsync"/>)
    /// </summary>
    public Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Пометить сущность как изменённую
    /// </summary>
    public void Update(T entity);

    /// <summary>
    /// Пометить сущность как удалённую
    /// </summary>
    public void Remove(T entity);

    /// <summary>
    /// Сохранить накопленные изменения одним батчем
    /// </summary>
    /// <returns>Количество затронутых записей</returns>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
