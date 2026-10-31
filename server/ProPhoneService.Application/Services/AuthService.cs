using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Вход по коду из email: запрос кода, подтверждение с созданием аккаунта и клиента, выдача и ротация токенов.
/// Другие каналы (SMS/мессенджеры) - в следующих спринтах за абстракцией INotifier
/// </summary>
public class AuthService(
    IAccountRepository accountRepository,
    IClientRepository clientRepository,
    IAuthCodeRepository authCodeRepository,
    INotifier notifier,
    TimeProvider timeProvider,
    AuthOptions authOptions,
    JwtOptions jwtOptions,
    IRefreshTokenStore refreshStore)
{
    private static readonly EmailAddressAttribute _emailValidator = new();

    /// <summary>
    /// Запросить одноразовый код на email (с ограничением частоты запросов)
    /// </summary>
    public async Task<AuthOutcome> RequestCodeAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeEmail(email, out var normalized))
        {
            return AuthOutcome.Failure(400, "Некорректный email", "Укажите корректный адрес электронной почты");
        }

        var authCode = await authCodeRepository.GetByEmailAsync(normalized, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (authCode is not null && authCode.LockedUntil is not null && authCode.LockedUntil > nowUtc)
        {
            return AuthOutcome.Failure(429, "Контакт заблокирован",
                $"Повторные попытки возможны после {FormatTime(authCode.LockedUntil.Value)} UTC");
        }

        if (authCode is not null
            && authCode.CreatedAt > nowUtc.AddSeconds(-authOptions.RequestCodeCooldownSeconds))
        {
            return AuthOutcome.Failure(429, "Код уже отправлен",
                "Повторный запрос кода возможен позже");
        }

        var code = GenerateCode();
        var expiresAt = nowUtc.AddMinutes(authOptions.CodeLifetimeMinutes);

        if (authCode is null)
        {
            authCode = new AuthCode
            {
                Id = Guid.NewGuid(),
                Email = normalized,
                Code = code,
                ExpiresAt = expiresAt,
                CreatedAt = nowUtc,
            };
            await authCodeRepository.AddAsync(authCode, cancellationToken);
        }
        else
        {
            authCode.Code = code;
            authCode.ExpiresAt = expiresAt;
            authCode.FailedAttempts = 0;
            authCode.LockedUntil = null;
            authCode.CreatedAt = nowUtc;
            authCodeRepository.Update(authCode);
        }

        await authCodeRepository.SaveChangesAsync(cancellationToken);
        await notifier.SendAsync(normalized, $"Код подтверждения: {code}. Действителен {authOptions.CodeLifetimeMinutes} мин", cancellationToken);

        return AuthOutcome.SuccessNoContent();
    }

    /// <summary>
    /// Подтвердить email кодом: верный код - аккаунт и клиент создаются (если email ещё нет)
    /// или используются существующие; выдаются access и refresh-токены
    /// </summary>
    public async Task<AuthOutcome> ConfirmAsync(
        string? email,
        string? code,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeEmail(email, out var normalized))
        {
            return AuthOutcome.Failure(400, "Некорректный email", "Укажите корректный адрес электронной почты");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return AuthOutcome.Failure(400, "Код не введён", "Введите код из сообщения");
        }

        var authCode = await authCodeRepository.GetByEmailAsync(normalized, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (authCode is null)
        {
            return AuthOutcome.Failure(400, "Код не запрошен", "Сначала запросите код подтверждения");
        }

        if (authCode.LockedUntil is not null && authCode.LockedUntil > nowUtc)
        {
            return AuthOutcome.Failure(429, "Контакт заблокирован",
                $"Повторные попытки возможны после {FormatTime(authCode.LockedUntil.Value)} UTC");
        }

        if (authCode.ExpiresAt < nowUtc)
        {
            return AuthOutcome.Failure(400, "Код истёк", "Запросите новый код подтверждения");
        }

        if (authCode.Code != code.Trim())
        {
            authCode.FailedAttempts++;
            var blocked = authCode.FailedAttempts >= authOptions.MaxFailedCodeAttempts;

            if (blocked)
            {
                authCode.LockedUntil = nowUtc.AddMinutes(authOptions.LockoutMinutes);
            }

            authCodeRepository.Update(authCode);
            await authCodeRepository.SaveChangesAsync(cancellationToken);

            return blocked
                ? AuthOutcome.Failure(429, "Контакт заблокирован",
                    $"Лимит неверных кодов ({authOptions.MaxFailedCodeAttempts}); разблокировка после {FormatTime(authCode.LockedUntil!.Value)} UTC")
                : AuthOutcome.Failure(400, "Неверный код", "Проверьте код из сообщения");
        }

        // Подтверждение email: клиент и аккаунт создаются (если email ещё нет) или используются существующие
        var client = await clientRepository.GetByEmailAsync(normalized, cancellationToken);

        var isNewClient = client is null;
        client ??= new Client
        {
            Id = Guid.NewGuid(),
            // Имя неизвестно на входе - до правки в ЛК отображается email
            Name = normalized,
            Email = normalized,
        };

        var account = await accountRepository.GetByClientIdAsync(client.Id, cancellationToken);
        var isNewAccount = account is null;
        account ??= new Account { Id = Guid.NewGuid(), ClientId = client.Id };

        if (isNewClient)
        {
            await clientRepository.AddAsync(client, cancellationToken);
        }

        if (isNewAccount)
        {
            await accountRepository.AddAsync(account, cancellationToken);
        }

        // Код одноразовый: погашаем
        authCodeRepository.Remove(authCode);

        await clientRepository.SaveChangesAsync(cancellationToken);

        return AuthOutcome.Success(IssueTokens(account.Id, client.Id, account.Role, nowUtc));
    }

    /// <summary>
    /// Ротация сессии: погасить старый refresh-токен и выдать новую пару токенов
    /// </summary>
    /// <returns>Новая сессия или <c>null</c>-сбой, если refresh-токен не найден, уже погашен или истёк</returns>
    public async Task<AuthOutcome> Refresh(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthOutcome.Failure(401, "Refresh-токен отсутствует", "Войдите заново");
        }

        var accountId = refreshStore.TryConsume(refreshToken);
        if (accountId is null)
        {
            return AuthOutcome.Failure(401, "Недействительный refresh-токен", "Войдите заново");
        }

        var account = await accountRepository.GetByIdAsync(accountId.Value, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        return account is null
            ? AuthOutcome.Failure(401, "Недействительный refresh-токен", "Войдите заново")
            : AuthOutcome.Success(IssueTokens(account.Id, account.ClientId, account.Role, nowUtc));
    }

    private (string AccessToken, DateTime ExpiresAtUtc) IssueAccessToken(
        Guid accountId,
        Guid clientId,
        AccountRole role,
        DateTime nowUtc)
    {
        var expiresAt = nowUtc.AddMinutes(authOptions.AccessTokenLifetimeMinutes);
        var securityKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key!));
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            securityKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims:
            [
                new Claim(AuthClaims.Subject, accountId.ToString()),
                new Claim(AuthClaims.ClientId, clientId.ToString()),
                new Claim(AuthClaims.Role, role.ToString()),
            ],
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private AuthTokens IssueTokens(Guid accountId, Guid clientId, AccountRole role, DateTime nowUtc)
    {
        var (accessToken, expiresAt) = IssueAccessToken(accountId, clientId, role, nowUtc);
        var refreshToken = refreshStore.Issue(
            accountId,
            timeProvider.GetUtcNow().AddDays(authOptions.RefreshTokenLifetimeDays));

        return new AuthTokens(accountId, clientId, role, accessToken, expiresAt, refreshToken);
    }

    private static string FormatTime(DateTime utc)
    {
        return utc.ToString("HH:mm");
    }

    private static string GenerateCode()
    {
        return RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
    }

    private static bool TryNormalizeEmail(string? email, out string normalized)
    {
        var candidate = email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(candidate) || !_emailValidator.IsValid(candidate))
        {
            normalized = "";
            return false;
        }

        normalized = candidate;
        return true;
    }
}
