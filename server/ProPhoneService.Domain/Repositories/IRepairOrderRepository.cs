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
    /// Получить страницу заказов клиента с фильтром по статусу, отсортированные по дате создания (новые сверху)
    /// </summary>
    /// <remarks>Для личного кабинета; неподтверждённые заявки не попадают</remarks>
    /// <param name="clientId">Идентификатор клиента</param>
    /// <param name="status">Статус для фильтра или <c>null</c> - все заказы клиента</param>
    Task<IReadOnlyList<RepairOrder>> GetPageByClientIdAsync(
        Guid clientId,
        RepairStatus? status,
        int skip = 0,
        int take = 25,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить заказ для смены статуса: загружается с клиентом (нужен телефон для уведомления)
    /// </summary>
    /// <returns>Заказ с загруженным клиентом или <c>null</c>, если не найден</returns>
    Task<RepairOrder?> GetForStatusChangeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Количество выданных заказов по типам устройств за период (по моменту перехода в Completed)
    /// </summary>
    /// <param name="fromUtc">Начало периода (UTC, включительно) или <c>null</c> - всё время</param>
    /// <param name="toUtc">Конец периода (UTC, включительно) или <c>null</c> - без верхней границы</param>
    Task<IReadOnlyDictionary<DeviceType, int>> CountCompletedByDeviceTypeAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сумма по заказам, завершённым за период (по моменту перехода в Completed)
    /// </summary>
    /// <param name="fromUtc">Начало периода (UTC, включительно) или <c>null</c> - всё время</param>
    /// <param name="toUtc">Конец периода (UTC, включительно) или <c>null</c> - без верхней границы</param>
    Task<decimal> SumCompletedOrdersAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
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

    /// <summary>
    /// Отменить просроченные неподтверждённые заявки и записать историю без уведомления
    /// </summary>
    Task<int> CancelExpiredPendingAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
