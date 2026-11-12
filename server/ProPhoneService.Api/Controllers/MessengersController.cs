using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Controllers;

/// <summary>
/// Ссылки привязки и защищённые входящие события ботов
/// </summary>
[ApiController]
[Route("api/messengers")]
public class MessengersController(MessengerBindingService bindings, MessengerOptions options) : ControllerBase
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
    /// Принять команду start только из личного чата и проверенного webhook Telegram
    /// </summary>
    [HttpPost("telegram/webhook")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Telegram(JsonElement update, CancellationToken cancellationToken)
    {
        if (!options.Telegram.IsConfigured
            || !SecretEquals(options.Telegram.WebhookSecret, Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString()))
            return Unauthorized();

        if (update.ValueKind != JsonValueKind.Object)
            return BadRequest();

        if (!update.TryGetProperty("message", out var message))
            return Ok();

        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("chat", out var chat)
            || chat.ValueKind != JsonValueKind.Object || Text(chat, "type") != "private"
            || !message.TryGetProperty("from", out var sender) || sender.ValueKind != JsonValueKind.Object
            || !Number(chat, "id", out var chatId) || !Number(sender, "id", out var senderId)
            || chatId != senderId || chatId <= 0)
            return Ok();

        var text = Text(message, "text");

        if (text is null || !text.StartsWith("/start ", StringComparison.Ordinal))
            return Ok();

        await bindings.BindAsync(MessengerChannel.Telegram, text[7..].Trim(), senderId, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// Подтвердить сервер Callback API и принять личное сообщение VK
    /// </summary>
    [HttpPost("vk/webhook")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Vk(JsonElement update, CancellationToken cancellationToken)
    {
        if (!options.Vk.IsConfigured || update.ValueKind != JsonValueKind.Object
            || !SecretEquals(options.Vk.WebhookSecret, Text(update, "secret"))
            || !Number(update, "group_id", out var groupId) || groupId != options.Vk.GroupId)
            return Unauthorized();

        var type = Text(update, "type");

        if (type == "confirmation")
            return Content(options.Vk.ConfirmationCode, "text/plain");

        if (type != "message_new")
            return Content("ok", "text/plain");

        if (!update.TryGetProperty("object", out var body) || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !Number(message, "from_id", out var senderId) || !Number(message, "peer_id", out var peerId)
            || senderId <= 0 || senderId != peerId)
            return Content("ok", "text/plain");

        await bindings.BindAsync(MessengerChannel.Vk, Text(message, "ref"), senderId, cancellationToken);

        return Content("ok", "text/plain");
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Number(JsonElement element, string property, out long result)
    {
        result = 0;

        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result);
    }

    private static bool SecretEquals(string expected, string? actual) => !string.IsNullOrWhiteSpace(actual)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    /// <summary>
    /// Выбор мессенджера для ссылки привязки
    /// </summary>
    public sealed record ChannelRequest
    {
        /// <summary>
        /// Выбранный канал Telegram или VK
        /// </summary>
        [Required]
        [EnumDataType(typeof(MessengerChannel))]
        public MessengerChannel? Channel { get; init; }
    }
}
