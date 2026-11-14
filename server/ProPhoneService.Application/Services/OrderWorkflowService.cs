using MapsterMapper;
using Microsoft.Extensions.Logging;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Exceptions;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Заказы на ремонт: чтение для панели мастера и личного кабинета клиента, смена статусов
/// </summary>
public class OrderWorkflowService(
    IRepairOrderRepository orderRepository,
    StatusNotificationService statusNotifications,
    IMapper mapper,
    ILogger<OrderWorkflowService> logger)
{
    /// <summary>
    /// Получить страницу заказов с фильтрами по статусу и диапазону желаемой даты визита (новые сверху)
    /// </summary>
    /// <param name="status">Статус для фильтра или <c>null</c> - все заказы</param>
    /// <param name="visitFrom">Начало диапазона по желаемой дате визита (UTC, необязательно)</param>
    /// <param name="visitTo">Конец диапазона по желаемой дате визита (UTC, включительно, необязательно)</param>
    /// <param name="page">Номер страницы (с 1)</param>
    public async Task<IReadOnlyList<RepairOrderListItemDto>> GetPageAsync(
        RepairStatus? status,
        DateTime? visitFrom,
        DateTime? visitTo,
        int page,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > OrderPaging.MaxPage)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, $"Номер страницы должен быть от 1 до {OrderPaging.MaxPage}");
        }

        var orders = await orderRepository.GetPageAsync(
            status,
            visitFrom,
            visitTo,
            skip: (page - 1) * OrderPaging.PageSize,
            take: OrderPaging.PageSize,
            cancellationToken);

        return mapper.Map<IReadOnlyList<RepairOrderListItemDto>>(orders);
    }

    /// <summary>
    /// Получить страницу заказов клиента (личный кабинет), новые сверху
    /// </summary>
    /// <param name="clientId">Клиент из клейма токена</param>
    /// <param name="status">Статус для фильтра или <c>null</c> - все заказы клиента</param>
    /// <param name="page">Номер страницы (с 1)</param>
    public async Task<IReadOnlyList<RepairOrderListItemDto>> GetClientPageAsync(
        Guid clientId,
        RepairStatus? status,
        int page,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > OrderPaging.MaxPage)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, $"Номер страницы должен быть от 1 до {OrderPaging.MaxPage}");
        }

        var orders = await orderRepository.GetPageByClientIdAsync(
            clientId,
            status,
            skip: (page - 1) * OrderPaging.PageSize,
            take: OrderPaging.PageSize,
            cancellationToken);

        return mapper.Map<IReadOnlyList<RepairOrderListItemDto>>(orders);
    }

    /// <summary>
    /// Получить карточку заказа по идентификатору: мастер видит любой заказ,
    /// клиент - только свой (чужой заказ неотличим от несуществующего - защита от IDOR)
    /// </summary>
    /// <param name="id">Идентификатор заказа</param>
    /// <param name="viewerClientId">Клиент из токена или <c>null</c> для неавторизованного</param>
    /// <param name="isMaster">Признак роли мастера (доступ ко всем заказам)</param>
    /// <returns>Карточка заказа или <c>null</c>, если заказ не найден или недоступен</returns>
    public async Task<RepairOrderDetailDto?> GetByIdAsync(
        Guid id,
        Guid? viewerClientId,
        bool isMaster,
        CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetWithDetailsAsync(id, cancellationToken);
        if (order is null
            || order.Status == RepairStatus.PendingContact
            || (!isMaster && order.ClientId != viewerClientId))
        {
            return null;
        }

        return mapper.Map<RepairOrderDetailDto>(order);
    }

    /// <summary>
    /// Сменить статус заказа по правилам машины состояний с записью в историю
    /// </summary>
    /// <param name="id">Идентификатор заказа</param>
    /// <param name="status">Целевой статус</param>
    /// <returns>Результат операции; <see cref="ChangeStatusResult.Success"/> - заказ сохранён</returns>
    public async Task<ChangeStatusResult> ChangeStatusAsync(
        Guid id,
        RepairStatus status,
        CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetForStatusChangeAsync(id, cancellationToken);
        if (order is null || order.Status == RepairStatus.PendingContact)
        {
            return ChangeStatusResult.NotFound;
        }

        try
        {
            order.ChangeStatus(status);
        }
        catch (InvalidStatusTransitionException)
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

        // Сбой провайдера уведомлений не должен ломать смену статуса: ошибки только логируются
        try
        {
            await statusNotifications.NotifyStatusChangedAsync(order, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Заказ {OrderId}: уведомление о смене статуса не отправлено", order.Id);
        }

        return ChangeStatusResult.Success;
    }
}
