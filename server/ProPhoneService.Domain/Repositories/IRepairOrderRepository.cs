using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Репозиторий заказов на ремонт
/// </summary>
public interface IRepairOrderRepository : IRepository<RepairOrder>
{
    /// <summary>
    /// Получить заказ вместе с историей статусов, услугами, устройством и клиентом
    /// </summary>
    /// <returns>Заказ с загруженными связями или <c>null</c>, если не найден</returns>
    Task<RepairOrder?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить неподтверждённые заявки с указанным контактом email (для привязки после подтверждения кода)
    /// </summary>
    Task<IReadOnlyList<RepairOrder>> GetPendingByEmailAsync(
        string contactEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить страницу заказов клиента, отсортированные по дате создания (новые сверху)
    /// </summary>
    /// <remarks>Для клиента (список заявок)</remarks>
    Task<IReadOnlyList<RepairOrder>> GetAllByClientIdAsync(
        Guid clientId,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить страницу заказов: фильтры по статусу и диапазону желаемой даты визита,
    /// отсортированные по дате создания (новые сверху)
    /// </summary>
    /// <remarks>Для панели мастера (список заявок)</remarks>
    /// <param name="status">Статус для фильтра или <c>null</c> - все заказы</param>
    /// <param name="visitFrom">Начало диапазона по желаемой дате визита (UTC, необязательно)</param>
    /// <param name="visitTo">Конец диапазона по желаемой дате визита (UTC, включительно, необязательно)</param>
    Task<IReadOnlyList<RepairOrder>> GetPageAsync(
        RepairStatus? status,
        DateTime? visitFrom,
        DateTime? visitTo,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default);
}
