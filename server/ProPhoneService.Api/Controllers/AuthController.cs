using Microsoft.AspNetCore.Mvc;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Api.Controllers;

/// <summary>
/// Вход по коду из email: запрос кода на почту, подтверждение, ротация refresh-токена
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AuthService authService, AuthOptions authOptions) : ControllerBase
{
    /// <summary>
    /// Запросить одноразовый код на email
    /// </summary>
    /// <param name="request">Email для получения кода</param>
    [HttpPost("request-code")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestCode(
        [FromBody] RequestCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var outcome = await authService.RequestCodeAsync(request.Email, cancellationToken);

        return MapOutcome(outcome);
    }

    /// <summary>
    /// Подтвердить email кодом: верный код - создаются аккаунт и клиент (если email ещё нет),
    /// выдаются access и refresh-токены (refresh - HttpOnly cookie)
    /// </summary>
    /// <param name="request">Email и код</param>
    [HttpPost("confirm")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        var outcome = await authService.ConfirmAsync(request.Email, request.Code, cancellationToken);

        return HandleSession(outcome);
    }

    /// <summary>
    /// Ротация сессии: погасить текущий refresh-токен и выдать новую пару токенов;
    /// повторное использование погашенного token даёт 401
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken = default)
    {
        var outcome = await authService.Refresh(GetRefreshToken(), cancellationToken);

        return HandleSession(outcome);
    }

    private ObjectResult MapOutcome(AuthOutcome outcome)
    {
        return StatusCode(outcome.StatusCode, new ProblemDetails
        {
            Title = outcome.ErrorTitle,
            Detail = outcome.ErrorDetail,
        });
    }

    private ObjectResult HandleSession(AuthOutcome outcome)
    {
        if (outcome.Tokens is null)
        {
            return StatusCode(outcome.StatusCode, new ProblemDetails
            {
                Title = outcome.ErrorTitle,
                Detail = outcome.ErrorDetail,
            });
        }

        SetRefreshCookie(outcome.Tokens.RefreshToken);

        return Ok(new AuthResponse(
            outcome.Tokens.AccountId,
            outcome.Tokens.ClientId,
            outcome.Tokens.AccessToken,
            outcome.Tokens.AccessTokenExpiresAtUtc));
    }

    private void SetRefreshCookie(string refreshToken)
    {
        Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(authOptions.RefreshTokenLifetimeDays),
            Path = "/api/auth",
        });
    }

    private string? GetRefreshToken()
    {
        return Request.Cookies["refresh_token"];
    }
}
