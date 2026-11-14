using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Вход и подтверждение заявки кодом, доставленным привязанному пользователю VK
/// </summary>
public class AuthService(
    IAccountRepository accountRepository,
    IClientRepository clientRepository,
    IAuthCodeRepository authCodeRepository,
    IRepairOrderRepository orderRepository,
    INotifier notifier,
    TimeProvider timeProvider,
    AuthOptions authOptions,
    JwtOptions jwtOptions,
    IRefreshTokenStore refreshStore,
    IMessengerBindingRepository bindings,
    StatusNotificationService statusNotifications)
{
    /// <summary>
    /// Запросить код через подтверждённую привязку VK с ограничением частоты
    /// </summary>
    public async Task<AuthOutcome> RequestCodeAsync(string? phone, CancellationToken cancellationToken = default,
        Guid? orderId = null)
    {
        if (!PhoneNormalizer.TryNormalize(phone, out var normalized))
        {
            return InvalidPhone();
        }

        return await authCodeRepository.ExecuteForPhoneAsync(normalized,
            () => RequestCodeCoreAsync(normalized, orderId, cancellationToken), cancellationToken);
    }

    private async Task<AuthOutcome> RequestCodeCoreAsync(string phone, Guid? orderId, CancellationToken ct)
    {
        var binding = orderId.HasValue
            ? await bindings.GetByOrderAsync(orderId.Value, ct)
            : await bindings.GetByPhoneAsync(phone, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (binding is null || binding.Phone != phone || binding.Channel != MessengerChannel.Vk
            || binding.RecipientId is null || binding.LinkedAt is null)
        {
            return AuthOutcome.Failure(400, "VK не привязан", "Сначала откройте диалог VK по ссылке из заявки");
        }

        if (binding.OrderId is { } pendingId)
        {
            var order = await orderRepository.GetByIdAsync(pendingId, ct);

            if (order is null || order.Status != RepairStatus.PendingContact || order.CreatedAt.AddMinutes(5) <= now)
            {
                return AuthOutcome.Failure(400, "Срок подтверждения истёк", "Оформите новую заявку");
            }
        }

        var authCode = await authCodeRepository.GetByPhoneAsync(phone, ct);

        if (authCode?.LockedUntil > now)
        {
            return AuthOutcome.Failure(429, "Контакт заблокирован", "Повторите попытку после окончания блокировки");
        }

        if (authCode is not null && authCode.CreatedAt > now.AddSeconds(-authOptions.RequestCodeCooldownSeconds))
        {
            return AuthOutcome.Failure(429, "Код уже отправлен", "Повторный запрос кода возможен позже");
        }

        var code = GenerateCode();
        await notifier.SendAsync(MessengerChannel.Vk, binding.RecipientId,
            NotificationMessages.ConfirmationCode(code), ct);

        if (authCode is null)
        {
            authCode = new AuthCode { Id = Guid.NewGuid(), Phone = phone, Code = code };
            await authCodeRepository.AddAsync(authCode, ct);
        }
        else
        {
            authCodeRepository.Update(authCode);
        }

        authCode.Code = code;
        authCode.BindingId = binding.Id;
        authCode.ExpiresAt = now.AddMinutes(5);
        authCode.CreatedAt = now;
        authCode.FailedAttempts = 0;
        authCode.LockedUntil = null;
        await authCodeRepository.SaveChangesAsync(ct);

        return AuthOutcome.SuccessNoContent();
    }

    /// <summary>
    /// Погасить код, активировать связанную заявку и выдать сессию аккаунта
    /// </summary>
    public async Task<AuthOutcome> ConfirmAsync(string? phone, string? code, CancellationToken cancellationToken = default)
    {
        if (!PhoneNormalizer.TryNormalize(phone, out var normalized))
        {
            return InvalidPhone();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return AuthOutcome.Failure(400, "Код не введён", "Введите код из сообщения VK");
        }

        var outcome = await authCodeRepository.ExecuteForPhoneAsync(normalized,
            () => ConfirmCoreAsync(normalized, code.Trim(), cancellationToken), cancellationToken);

        // ExecuteForPhoneAsync уже зафиксировал транзакцию с заявкой, аккаунтом и погашенным кодом
        if (outcome.ActivatedOrder is not null)
        {
            await statusNotifications.NotifyStatusChangedAsync(outcome.ActivatedOrder, cancellationToken);
        }

        return outcome;
    }

    private async Task<AuthOutcome> ConfirmCoreAsync(string phone, string code, CancellationToken ct)
    {
        var authCode = await authCodeRepository.GetByPhoneAsync(phone, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (authCode is null)
        {
            return AuthOutcome.Failure(400, "Код не запрошен", "Сначала запросите код подтверждения");
        }

        if (authCode.LockedUntil > now)
        {
            return AuthOutcome.Failure(429, "Контакт заблокирован", "Повторите попытку после окончания блокировки");
        }

        if (authCode.ExpiresAt <= now)
        {
            return AuthOutcome.Failure(400, "Код истёк", "Запросите новый код подтверждения");
        }

        if (authCode.Code != code)
        {
            authCode.FailedAttempts++;
            var blocked = authCode.FailedAttempts >= authOptions.MaxFailedCodeAttempts;
            if (blocked)
            {
                authCode.LockedUntil = now.AddMinutes(authOptions.LockoutMinutes);
            }

            authCodeRepository.Update(authCode);
            await authCodeRepository.SaveChangesAsync(ct);

            return AuthOutcome.Failure(blocked ? 429 : 400, blocked ? "Контакт заблокирован" : "Неверный код",
                "Проверьте код из сообщения VK");
        }

        var binding = await bindings.GetByIdAsync(authCode.BindingId, ct);
        if (binding is null || binding.Phone != phone || binding.Channel != MessengerChannel.Vk
            || binding.RecipientId is null || binding.LinkedAt is null)
        {
            return AuthOutcome.Failure(400, "Привязка отсутствует", "Повторите привязку VK");
        }

        RepairOrder? pending = null;
        if (binding.OrderId is { } orderId)
        {
            pending = await orderRepository.GetWithDetailsAsync(orderId, ct);

            if (pending is null || pending.ContactPhone != phone || pending.Status != RepairStatus.PendingContact
                || pending.CreatedAt.AddMinutes(5) <= now)
            {
                return AuthOutcome.Failure(400, "Срок подтверждения истёк", "Оформите новую заявку");
            }
        }

        Account? account;
        Client? client;
        if (binding.AccountId is { } boundAccountId)
        {
            account = await accountRepository.GetByIdAsync(boundAccountId, ct);
            client = account is null ? null : await clientRepository.GetByIdAsync(account.ClientId, ct);

            if (account is null || client is null || (!string.IsNullOrWhiteSpace(client.Phone) && client.Phone != phone))
            {
                return AuthOutcome.Failure(403, "Нет доступа", "Привязка не соответствует аккаунту");
            }

            if (string.IsNullOrWhiteSpace(client.Phone))
            {
                client.Phone = phone;
            }
        }
        else
        {
            client = await clientRepository.GetByPhoneAsync(phone, ct);
            account = client is null ? null : await accountRepository.GetByClientIdAsync(client.Id, ct);
        }

        if (account is null && pending is null)
        {
            return AuthOutcome.Failure(400, "Аккаунт не найден", "Сначала оформите и подтвердите заявку");
        }

        if (account?.Role == AccountRole.Master
            && (authOptions.MasterVkUserId <= 0 || binding.RecipientId != authOptions.MasterVkUserId.ToString(CultureInfo.InvariantCulture)))
        {
            return AuthOutcome.Failure(403, "Нет доступа", "Для мастера требуется настроенная привязка VK");
        }

        if (client is null)
        {
            client = new Client { Id = Guid.NewGuid(), Name = pending?.ContactName ?? phone, Phone = phone };
            await clientRepository.AddAsync(client, ct);
        }
        else if (string.IsNullOrWhiteSpace(client.Name) && !string.IsNullOrWhiteSpace(pending?.ContactName))
        {
            client.Name = pending.ContactName;
        }

        if (account is null)
        {
            account = new Account { Id = Guid.NewGuid(), ClientId = client.Id };
            await accountRepository.AddAsync(account, ct);
        }

        if (pending is not null)
        {
            pending.ConfirmClient(client.Id);
        }

        binding.OrderId = null;
        binding.AccountId = account.Id;
        authCodeRepository.Remove(authCode);
        await clientRepository.SaveChangesAsync(ct);

        return AuthOutcome.Success(IssueTokens(account.Id, client.Id, account.Role, now), pending);
    }

    private static AuthOutcome InvalidPhone() =>
        AuthOutcome.Failure(400, "Некорректный телефон", "Укажите телефон в формате +7XXXXXXXXXX");

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
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key!));
        var credentials = new SigningCredentials(
            securityKey, SecurityAlgorithms.HmacSha256);

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

    private static string GenerateCode()
    {
        return RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

}
