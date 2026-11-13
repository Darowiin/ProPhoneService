using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Infrastructure.EfCore.Notifications;

/// <summary>
/// Последовательный опрос сообщества VK с восстановлением сессии и повтором обработки
/// </summary>
public class VkLongPollService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory,
    MessengerOptions options, ILogger<VkLongPollService> logger) : BackgroundService
{
    /// <summary>
    /// Получать события до остановки приложения
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Vk.IsConfigured)
        {
            logger.LogWarning("VK Long Poll выключен: задайте Token и GroupId");
            return;
        }

        using var http = httpClientFactory.CreateClient("VkLongPoll");
        var client = new VkLongPollClient(http, options);
        VkLongPollSession? session = null;
        JsonElement[]? pending = null;
        string? nextTs = null;
        var index = 0;
        var stage = "open";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (session is null)
                {
                    stage = "open";
                    session = await client.OpenAsync(stoppingToken);
                    logger.LogInformation("VK Long Poll запущен");
                }

                if (pending is null)
                {
                    stage = "poll";
                    var response = await client.PollAsync(session, stoppingToken);

                    if (response.TryGetProperty("failed", out var failed))
                    {
                        if (failed.ValueKind != JsonValueKind.Number || !failed.TryGetInt32(out var code))
                        {
                            throw new VkLongPollException(0);
                        }

                        if (code == 1)
                        {
                            session.Ts = VkLongPollClient.String(response, "ts") ?? throw new VkLongPollException(0);
                            logger.LogWarning("VK Long Poll: история недоступна, позиция обновлена");
                        }
                        else if (code == 2)
                        {
                            var renewed = await client.OpenAsync(stoppingToken);
                            renewed.Ts = session.Ts;
                            session = renewed;
                        }
                        else if (code == 3)
                        {
                            session = null;
                        }
                        else
                        {
                            throw new VkLongPollException(code);
                        }

                        continue;
                    }

                    nextTs = VkLongPollClient.String(response, "ts");

                    if (string.IsNullOrEmpty(nextTs) || !response.TryGetProperty("updates", out var updates)
                        || updates.ValueKind != JsonValueKind.Array)
                    {
                        throw new VkLongPollException(0);
                    }

                    pending = updates.EnumerateArray().Select(item => item.Clone()).ToArray();
                    index = 0;
                }

                while (index < pending.Length)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    stage = "handle";
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<IVkUpdateHandler>();
                    await handler.HandleAsync(pending[index], stoppingToken);
                    index++;
                }

                session.Ts = nextTs!;
                pending = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Не выводим сообщение исключения, payload, ключ сессии или токен сообщества
                logger.LogWarning("VK Long Poll: этап {Stage}, тип {Type}, код {Code}, стек {Stack}",
                    stage, ex.GetType().Name, ex is VkLongPollException vk ? vk.Code : 0, ex.StackTrace);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
