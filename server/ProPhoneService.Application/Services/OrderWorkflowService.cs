using MapsterMapper;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Exceptions;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Заказы на ремонт: чтение для панели мастера и смена статусов
/// </summary>
public class OrderWorkflowService(
    IRepairOrderRepository orderRepository,
    IMapper mapper)
{
    private const int PageSize = 25;

    /// <summary>
    /// Получить страницу заказов с фильтром по статусу (новые сверху)
    /// </summary>
    /// <param name="status">Статус для фильтра или <c>null</c> - все заказы</param>
    /// <param name="page">Номер страницы (с 1)</param>
    public async Task<IReadOnlyList<RepairOrderListItemDto>> GetPageAsync(
        RepairStatus? status,
        int page,
        CancellationToken cancellationToken = default)
    {
        var orders = await orderRepository.GetPageAsync(
            status,
            skip: (page - 1) * PageSize,
            take: PageSize,
            cancellationToken);

        return mapper.Map<IReadOnlyList<RepairOrderListItemDto>>(orders);
    }

    /// <summary>
    /// Сменить статус заказа по правилам машины состояний с записью в историю
    /// </summary>
    /// <param name="id">Идентификатор заказа</param>
    /// <param name="status">Целевой статус</param>
    /// <param name="comment">Комментарий к смене (необязательно)</param>
    /// <returns>Результат операции; <see cref="ChangeStatusResult.Success"/> - заказ сохранён</returns>
    public async Task<ChangeStatusResult> ChangeStatusAsync(
        Guid id,
        RepairStatus status,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetWithDetailsAsync(id, cancellationToken);
        if (order is null)
        {
            return ChangeStatusResult.NotFound;
        }

        try
        {
            order.ChangeStatus(status, comment);
        }
        catch (InvalidOperationException)
        {
            return ChangeStatusResult.InvalidTransition;
        }

        try
        {
            await orderRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Токен конкурентности (xmin) поймал параллельную модификацию заказа
            return ChangeStatusResult.Conflict;
        }

        return ChangeStatusResult.Success;
    }
}
