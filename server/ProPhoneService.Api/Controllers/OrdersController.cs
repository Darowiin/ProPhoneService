using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProPhoneService.Api.Contracts;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Controllers;

/// <summary>
/// Заказы на ремонт: панель мастера (только Master) и карточка заказа (свой - клиенту, любой - мастеру)
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrdersController(OrderWorkflowService ordersService, OrderCreationService orderCreationService) : ControllerBase
{
    /// <summary>
    /// Получить страницу заказов с фильтрами по статусу и диапазону желаемой даты визита (новые сверху)
    /// </summary>
    /// <param name="status">Фильтр по статусу; без параметра - все заказы</param>
    /// <param name="visitFrom">Начало диапазона по желаемой дате визита (UTC ISO 8601; необязательно)</param>
    /// <param name="visitTo">Конец диапазона по желаемой дате визита (UTC ISO 8601, включительно; необязательно)</param>
    /// <param name="page">Номер страницы, начиная с 1 (по умолчанию 1)</param>
    [Authorize(Roles = nameof(AccountRole.Master))]
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RepairOrderListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<RepairOrderListItemDto>>> GetPage(
        [FromQuery] RepairStatus? status = null,
        [FromQuery] DateTimeOffset? visitFrom = null,
        [FromQuery] DateTimeOffset? visitTo = null,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > OrderWorkflowService.MaxPage)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Некорректный номер страницы",
                Detail = "Параметр page должен быть целым числом, начиная с 1",
            });
        }

        if (visitFrom is not null && visitTo is not null && visitFrom > visitTo)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Некорректный диапазон дат",
                Detail = "Параметр visitFrom должен быть не позже параметра visitTo",
            });
        }

        var pageItems = await ordersService.GetPageAsync(
            status,
            visitFrom?.UtcDateTime,
            visitTo?.UtcDateTime,
            page,
            cancellationToken);

        return Ok(pageItems);
    }

    /// <summary>
    /// Получить карточку заказа: клиент, устройство, услуги и тайм-лайн статусов
    /// </summary>
    /// <remarks>Клиенту доступен только собственный заказ: чужой идентификатор даёт 404 (защита от IDOR)</remarks>
    /// <param name="id">Идентификатор заказа</param>
    [Authorize]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<RepairOrderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepairOrderDetailDto>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var isMaster = User.IsInRole(nameof(AccountRole.Master));
        var viewerClientId = Guid.TryParse(User.FindFirst(AuthClaims.ClientId)?.Value, out var claimClientId)
            ? claimClientId
            : (Guid?)null;

        var card = await ordersService.GetByIdAsync(id, viewerClientId, isMaster, cancellationToken);

        if (card is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Заказ не найден",
                Detail = "Заказ не существует",
            });
        }

        return Ok(card);
    }

    /// <summary>
    /// Сменить статус заказа по правилам машины состояний
    /// </summary>
    /// <remarks>Запрещённый переход (перепрыгнуть этап или вернуть назад) возвращает 409</remarks>
    /// <param name="id">Идентификатор заказа</param>
    /// <param name="request">Целевой статус и комментарий</param>
    [Authorize(Roles = nameof(AccountRole.Master))]
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ChangeStatus(
        Guid id,
        [FromBody] ChangeStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await ordersService.ChangeStatusAsync(id, request.Status, request.Comment, cancellationToken);

        return result switch
        {
            ChangeStatusResult.NotFound => NotFound(new ProblemDetails
            {
                Title = "Заказ не найден",
                Detail = "Заказ не существует",
            }),
            ChangeStatusResult.InvalidTransition => Conflict(new ProblemDetails
            {
                Title = "Запрещённый переход статуса",
                Detail = $"Переход в статус {request.Status} недопустим по правилам машины состояний",
            }),
            ChangeStatusResult.Conflict => Conflict(new ProblemDetails
            {
                Title = "Заказ был изменён",
                Detail = "Заказ изменён другим пользователем, обновите страницу и повторите",
            }),
            ChangeStatusResult.Success => NoContent(),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Создать заявку на ремонт
    /// </summary>
    /// <remarks>
    /// Без токена заявка создаётся неподтверждённой (PendingContact) и активируется кодом
    /// на email, который уходит через INotifier; с токеном клиент привязывается сразу
    /// </remarks>
    /// <param name="request">Данные заявки</param>
    [AllowAnonymous]
    [HttpPost]
    [ProducesResponseType<CreateOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateOrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var clientId = Guid.TryParse(User.FindFirst(AuthClaims.ClientId)?.Value, out var claimClientId)
            ? claimClientId
            : (Guid?)null;

        var result = await orderCreationService.CreateAsync(request, clientId, cancellationToken);

        if (!result.IsSuccess)
        {
            var details = new ValidationProblemDetails(
                result.Errors!.ToDictionary(kv => kv.Key, kv => kv.Value));

            return ValidationProblem(details);
        }

        var orderId = result.OrderId!.Value;

        return CreatedAtAction(
            nameof(GetById),
            new { id = orderId },
            new CreateOrderResponse(orderId, result.RequiresEmailConfirmation));
    }
}
