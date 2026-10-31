using ProPhoneService.Domain.Shared.Auditing;
using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Api.Auth;

/// <summary>
/// Аккаунт текущего запроса из клейма <c>sub</c> access-токена
/// </summary>
public sealed class HttpContextCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    /// <inheritdoc />
    public Guid? AccountId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirst(AuthClaims.Subject)?.Value;

            return Guid.TryParse(value, out var accountId) ? accountId : null;
        }
    }
}
