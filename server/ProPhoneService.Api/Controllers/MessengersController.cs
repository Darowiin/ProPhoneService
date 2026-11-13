using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Controllers;

/// <summary>
/// Ссылки привязки к сообществу VK
/// </summary>
[ApiController]
[Route("api/messengers")]
public class MessengersController(MessengerBindingService bindings) : ControllerBase
{
    /// <summary>
    /// Получить ссылку в бота после создания гостевой заявки
    /// </summary>
    [HttpPost("orders/{orderId:guid}/link")]
    [EnableRateLimiting("messenger-links")]
    public async Task<IActionResult> CreateOrderLink(Guid orderId, ChannelRequest request, CancellationToken cancellationToken)
    {
        return await CreateLink(() => bindings.CreateForOrderAsync(orderId, request.Channel!.Value, cancellationToken));
    }

    /// <summary>
    /// Получить ссылку в бота для текущего аккаунта
    /// </summary>
    [Authorize]
    [HttpPost("me/link")]
    [EnableRateLimiting("messenger-links")]
    public async Task<IActionResult> CreateAccountLink(ChannelRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(AuthClaims.Subject)?.Value, out var accountId))
            return Unauthorized();

        return await CreateLink(() => bindings.CreateForAccountAsync(accountId, request.Channel!.Value, cancellationToken));
    }

    private async Task<IActionResult> CreateLink(Func<Task<MessengerLinkResponse?>> operation)
    {
        Response.Headers.CacheControl = "no-store";

        try
        {
            var result = await operation();

            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Некорректный контакт", Detail = ex.Message });
        }
    }

    /// <summary>
    /// Канал ссылки привязки
    /// </summary>
    public sealed record ChannelRequest
    {
        /// <summary>
        /// Единственный поддерживаемый канал VK
        /// </summary>
        [Required]
        [EnumDataType(typeof(MessengerChannel))]
        public MessengerChannel? Channel { get; init; } = MessengerChannel.Vk;
    }
}
