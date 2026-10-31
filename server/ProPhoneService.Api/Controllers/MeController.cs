using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Controllers;

/// <summary>
/// Личный кабинет клиента: только собственные заказы (клиент - из клейма токена)
/// </summary>
[ApiController]
[Authorize]
[Route("api/me")]
public class MeController(OrderWorkflowService ordersService) : ControllerBase
{
    /// <summary>
    /// Получить страницу заказов клиента (новые сверху)
    /// </summary>
    /// <param name="status">Фильтр по статусу; без параметра - все заказы клиента</param>
    /// <param name="page">Номер страницы, начиная с 1 (по умолчанию 1)</param>
    [HttpGet("orders")]
    [ProducesResponseType<IReadOnlyList<RepairOrderListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<RepairOrderListItemDto>>> GetOrders(
        [FromQuery] RepairStatus? status = null,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetClientId(out var clientId))
        {
            return MissingClientClaim();
        }

        if (page < 1 || page > OrderPaging.MaxPage)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Некорректный номер страницы",
                Detail = "Параметр page должен быть целым числом, начиная с 1",
            });
        }

        var orders = await ordersService.GetClientPageAsync(clientId, status, page, cancellationToken);

        return Ok(orders);
    }

    /// <summary>
    /// Получить карточку своего заказа: устройство, услуги, стоимость и тайм-лайн статусов
    /// </summary>
    /// <remarks>Чужой или неподтверждённый заказ неотличим от несуществующего - 404</remarks>
    /// <param name="id">Идентификатор заказа</param>
    [HttpGet("orders/{id:guid}")]
    [ProducesResponseType<RepairOrderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RepairOrderDetailDto>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetClientId(out var clientId))
        {
            return MissingClientClaim();
        }

        var card = await ordersService.GetByIdAsync(id, clientId, isMaster: false, cancellationToken);

        if (card is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Заказ не найден",
                Detail = "Заказ не существует или недоступен",
            });
        }

        return Ok(card);
    }

    private bool TryGetClientId(out Guid clientId)
    {
        return Guid.TryParse(User.FindFirst(AuthClaims.ClientId)?.Value, out clientId);
    }

    private ObjectResult MissingClientClaim()
    {
        return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Title = "Нет доступа",
            Detail = "В токене отсутствует идентификатор клиента",
        });
    }
}
