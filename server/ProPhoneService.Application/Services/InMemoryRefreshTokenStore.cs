using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Хранилище refresh-токенов: выдача, ротация (старый токен погашается при выдаче нового),
/// повторное использование погашенного токена отклоняется.
/// Хранение в памяти: при перезапуске все сессии завершаются
/// </summary>
public class InMemoryRefreshTokenStore(TimeProvider timeProvider) : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, (Guid AccountId, DateTimeOffset ExpiresAtUtc)> _tokens = new();

    /// <inheritdoc />
    public string Issue(Guid accountId, DateTimeOffset expiresAtUtc)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = (accountId, expiresAtUtc);

        return token;
    }

    /// <inheritdoc />
    public Guid? TryConsume(string? token)
    {
        if (token is null || !_tokens.TryGetValue(token, out var entry))
        {
            return null;
        }

        _tokens.TryRemove(token, out _);

        return entry.ExpiresAtUtc <= timeProvider.GetUtcNow() ? null : entry.AccountId;
    }
}
