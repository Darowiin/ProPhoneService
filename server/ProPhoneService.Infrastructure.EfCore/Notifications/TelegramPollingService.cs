using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Фоновый опрос Telegram с последовательной обработкой и подтверждением событий
/// </summary>
public class TelegramPollingService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory,
    MessengerOptions options, ILogger<TelegramPollingService> logger) : BackgroundService
{
    /// <summary>
    /// Выполнять long polling до остановки приложения
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Telegram.UpdateMode != TelegramUpdateMode.Polling)
        {
            return;
        }

        if (!options.Telegram.IsConfigured)
        {
            logger.LogWarning("Telegram long polling выключен: задайте Token и BotUsername");
            return;
        }

        using var httpClient = httpClientFactory.CreateClient("TelegramPolling");
        var client = new TelegramPollingClient(httpClient, options);
        var webhookRemoved = false;
        long? offset = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var retryDelay = TimeSpan.FromSeconds(5);

            try
            {
                if (!webhookRemoved)
                {
                    await client.DeleteWebhookAsync(stoppingToken);
                    webhookRemoved = true;

                    logger.LogInformation("Telegram long polling запущен, webhook отключён");
                }

                var updates = await client.GetUpdatesAsync(offset, stoppingToken);

                foreach (var update in updates.OrderBy(item => item.GetProperty("update_id").GetInt64()))
                {
                    var updateId = update.GetProperty("update_id").GetInt64();

                    if (offset.HasValue && updateId < offset.Value)
                    {
                        continue;
                    }

                    await using var scope = scopeFactory.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<ITelegramUpdateHandler>();

                    await handler.HandleAsync(update, stoppingToken);

                    // Следующий getUpdates подтверждает только успешно обработанные события
                    offset = updateId + 1;
                }

                continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TelegramPollingException ex)
            {
                retryDelay = ex.RetryAfter;

                logger.LogWarning("Telegram polling: ошибка {ErrorCode}, повтор через {Delay} секунд",
                    ex.ErrorCode, retryDelay.TotalSeconds);
            }
            catch (Exception)
            {
                // Не логируем payload или исключение с потенциально чувствительными данными
                logger.LogWarning("Telegram polling: событие не обработано, повтор без подтверждения offset");
            }

            try
            {
                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
