using Microsoft.Extensions.Logging;
using ProPhoneService.Application.Contracts;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Демо-реализация отправки сообщений: пишет в лог; используется, когда email-провайдер не настроен
/// </summary>
public class LoggingNotifier(ILogger<LoggingNotifier> logger) : INotifier
{
    /// <inheritdoc />
    public Task SendAsync(
        string target,
        string subject,
        string text,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Отправка сообщения: {Target} [{Subject}] -> {Text}", target, subject, text);
        return Task.CompletedTask;
    }
}
