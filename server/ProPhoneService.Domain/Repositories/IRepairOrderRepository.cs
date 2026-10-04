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
    public Task<RepairOrder?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить страницу заказов клиента, отсортированные по дате создания (новые сверху)
    /// </summary>
    /// <remarks>Для клиента (список заявок)</remarks>
    public Task<IReadOnlyList<RepairOrder>> GetAllByClientIdAsync(
        Guid clientId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить страницу заказов с отфильтрованным статусом, отсортированные по дате создания (новые сверху)
    /// </summary>
    /// <remarks>Для панели мастера (список заявок)</remarks>
    public Task<IReadOnlyList<RepairOrder>> GetByStatusAsync(
        RepairStatus status,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
}
