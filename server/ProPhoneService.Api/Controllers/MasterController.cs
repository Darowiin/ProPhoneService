using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Controllers;

/// <summary>
/// Панель мастера: статистика дашборда
/// </summary>
[ApiController]
[Authorize(Roles = nameof(AccountRole.Master))]
[Route("api/master")]
public class MasterController(StatisticsService statistics) : ControllerBase
{
    /// <summary>
    /// Статистика завершённых ремонтов за период: количество по типам устройств и общая сумма
    /// </summary>
    /// <param name="period">Период: Month (с начала месяца, по умолчанию), Quarter (с начала квартала), All (всё время)</param>
    [HttpGet("stats")]
    [ProducesResponseType<StatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StatsDto>> GetStats(
        [FromQuery] StatsPeriod period = StatsPeriod.Month,
        CancellationToken cancellationToken = default)
    {
        return Ok(await statistics.GetAsync(period, cancellationToken));
    }
}
