using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProPhoneService.Domain.Repositories;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Отмена неподтверждённых заявок независимо от открытого браузера
/// </summary>
public class PendingOrderExpirationService(IServiceScopeFactory scopes, TimeProvider timeProvider,
    ILogger<PendingOrderExpirationService> logger) : BackgroundService
{
    /// <summary>
    /// Проверять срок подтверждения каждые 15 секунд
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), timeProvider);

        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var orders = scope.ServiceProvider.GetRequiredService<IRepairOrderRepository>();
                await orders.CancelExpiredPendingAsync(timeProvider.GetUtcNow().UtcDateTime, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Отмена неподтверждённых заявок: ошибка {Type}, повтор через 15 секунд", ex.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
