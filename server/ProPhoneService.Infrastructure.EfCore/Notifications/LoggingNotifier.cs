using Microsoft.Extensions.Logging;
using ProPhoneService.Application.Contracts;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Демо-реализация отправки сообщений: пишет в лог (реальные провайдеры - Карточка 5)
/// </summary>
public class LoggingNotifier(ILogger<LoggingNotifier> logger) : INotifier
{
    /// <inheritdoc />
    public Task SendAsync(string target, string text, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Отправка сообщения: {Target} -> {Text}", target, text);
        return Task.CompletedTask;
    }
}
