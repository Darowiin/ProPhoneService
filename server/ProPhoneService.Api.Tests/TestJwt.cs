using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

/// <summary>
/// Access-токены для endpoint-тестов: та же подпись, issuer и audience, что у API
/// </summary>
internal static class TestJwt
{
    /// <summary>
    /// Ключ подписи тестовых токенов (подставляется в хост через <c>Jwt__Key</c>)
    /// </summary>
    public const string Key = "endpoint-tests-signing-key-32-characters-minimum";

    /// <summary>
    /// Токен мастера (доступ ко всем заказам и правке прайса)
    /// </summary>
    public static string ForMaster(Guid? accountId = null)
    {
        return Create(accountId ?? Guid.NewGuid(), Guid.NewGuid(), AccountRole.Master);
    }

    /// <summary>
    /// Токен клиента с заданным идентификатором клиента
    /// </summary>
    public static string ForClient(Guid clientId, Guid? accountId = null)
    {
        return Create(accountId ?? Guid.NewGuid(), clientId, AccountRole.Client);
    }

    /// <summary>
    /// Токен, подписанный чужим ключом (проверка отклонения подделки)
    /// </summary>
    public static string ForgedForMaster()
    {
        return Create(Guid.NewGuid(), Guid.NewGuid(), AccountRole.Master, "forged-signing-key-32-characters-mimimum");
    }

    /// <summary>
    /// Приложить токен к клиенту
    /// </summary>
    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static string Create(Guid accountId, Guid clientId, AccountRole role, string? key = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "ProPhoneService",
            audience: "ProPhoneService",
            claims:
            [
                new Claim(AuthClaims.Subject, accountId.ToString()),
                new Claim(AuthClaims.ClientId, clientId.ToString()),
                new Claim(AuthClaims.Role, role.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
