using Microsoft.AspNetCore.Mvc;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;

namespace ProPhoneService.Api.Controllers;

/// <summary>
/// Прайс-лист мастерской
/// </summary>
[ApiController]
[Route("api/services")]
public class ServicesController(PriceListService priceList) : ControllerBase
{
    /// <summary>
    /// Получить активные услуги прайс-листа
    /// </summary>
    /// <remarks>Список кэшируется на 5 минут; кэш сбрасывается при правке прайса</remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ServiceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ServiceDto>>> GetAll(CancellationToken cancellationToken)
    {
        var services = await priceList.GetActiveAsync(cancellationToken);

        return Ok(services);
    }

    /// <summary>
    /// Создать услугу прайс-листа
    /// </summary>
    /// <param name="request">Данные услуги</param>
    [HttpPost]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceDto>> Create(
        [FromBody] ServiceUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var service = await priceList.CreateAsync(request, cancellationToken);

        return Created($"/api/services/{service.Id}", service);
    }

    /// <summary>
    /// Обновить услугу прайс-листа
    /// </summary>
    /// <param name="id">Идентификатор услуги</param>
    /// <param name="request">Данные услуги</param>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceDto>> Update(
        Guid id,
        [FromBody] ServiceUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var service = await priceList.UpdateAsync(id, request, cancellationToken);

        if (service is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Услуга не найдена",
                Detail = $"Услуга {id} не существует",
            });
        }

        return Ok(service);
    }

    /// <summary>
    /// Скрыть услугу из прайс-листа (реальное удаление не применяется)
    /// </summary>
    /// <param name="id">Идентификатор услуги</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Hide(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var hidden = await priceList.HideAsync(id, cancellationToken);

        if (!hidden)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Услуга не найдена",
                Detail = $"Услуга {id} не существует",
            });
        }

        return NoContent();
    }
}
