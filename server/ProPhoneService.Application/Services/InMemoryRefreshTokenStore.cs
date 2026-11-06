using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Хранилище refresh-токенов: выдача, ротация (старый токен погашается при выдаче нового),
/// повторное использование погашенного токена отклоняется.
/// Хранение в памяти: при перезапуске все сессии завершаются
/// </summary>
public class InMemoryRefreshTokenStore : IRefreshTokenStore, IDisposable
{
    private readonly ConcurrentDictionary<string, (Guid AccountId, DateTimeOffset ExpiresAtUtc)> _tokens = new();

    private readonly TimeProvider _timeProvider;
    private readonly ITimer _cleanupTimer;

    /// <summary>
    /// Создать хранилище с ежеминутной очисткой истёкших сессий
    /// </summary>
    public InMemoryRefreshTokenStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _cleanupTimer = timeProvider.CreateTimer(
            _ => RemoveExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void RemoveExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in _tokens)
        {
            if (entry.Value.ExpiresAtUtc <= now)
            {
                _tokens.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _cleanupTimer.Dispose();

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
        if (token is null || !_tokens.TryRemove(token, out var entry))
        {
            return null;
        }

        return entry.ExpiresAtUtc <= _timeProvider.GetUtcNow() ? null : entry.AccountId;
    }
}
