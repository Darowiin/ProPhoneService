using Microsoft.Extensions.Logging;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Доставка уведомлений о сохранённых статусах через VK с повторами и журналом результата
/// </summary>
public class StatusNotificationService(
    INotifier notifier,
    IRepository<NotificationLog> notificationRepository,
    NotificationOptions notificationOptions,
    ILogger<StatusNotificationService> logger,
    IMessengerBindingRepository bindings)
{
    /// <summary>
    /// Уведомить владельца заявки после сохранения изменений, не откатывая их при сбое доставки
    /// </summary>
    public async Task NotifyStatusChangedAsync(RepairOrder order, CancellationToken cancellationToken = default)
    {
        try
        {
            await NotifyCoreAsync(order, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Ошибка журнала или поиска получателя не отменяет уже сохранённую заявку
            logger.LogError("Заказ {OrderId}: уведомление не обработано, тип ошибки {ErrorType}",
                order.Id, ex.GetType().Name);
        }
    }

    private async Task NotifyCoreAsync(RepairOrder order, CancellationToken cancellationToken)
    {
        var notification = NotificationMessages.Status(order.Status, order.OrderNumber);
        if (notification is null || (order.Status == RepairStatus.Cancelled && order.ClientId is null))
        {
            return;
        }

        // Источник получателя - аккаунт владельца, произвольный телефон из формы не используется
        var binding = order.ClientId is { } clientId
            ? await bindings.GetByClientIdAsync(clientId, cancellationToken)
            : null;
        var recipient = binding?.Channel == MessengerChannel.Vk && binding.LinkedAt is not null
            ? binding.RecipientId : null;
        var (subject, message) = notification.Value;
        var attempts = 0;
        var status = NotificationDeliveryStatus.Failed;
        var maxAttempts = Math.Max(1, notificationOptions.MaxAttempts);

        if (!string.IsNullOrWhiteSpace(recipient))
        {
            while (attempts < maxAttempts)
            {
                attempts++;

                try
                {
                    await notifier.SendAsync(MessengerChannel.Vk, recipient, message, cancellationToken);
                    status = NotificationDeliveryStatus.Sent;
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Заказ {OrderId}: попытка VK {Attempt} из {MaxAttempts}, ошибка {ErrorType}",
                        order.Id, attempts, maxAttempts, ex.GetType().Name);

                    if (attempts < maxAttempts)
                    {
                        var delay = notificationOptions.RetryDelay.TotalMilliseconds * Math.Pow(2, attempts - 1);
                        if (delay > 0)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
                        }
                    }
                }
            }
        }

        await notificationRepository.AddAsync(new NotificationLog
        {
            Id = Guid.NewGuid(),
            RepairOrderId = order.Id,
            Channel = MessengerChannel.Vk,
            Recipient = recipient,
            Subject = subject,
            Message = message,
            Status = status,
            Attempts = attempts,
        }, cancellationToken);
        await notificationRepository.SaveChangesAsync(cancellationToken);

        if (status == NotificationDeliveryStatus.Failed)
        {
            logger.LogWarning("Заказ {OrderId}: уведомление VK не доставлено, попыток {Attempts}", order.Id, attempts);
        }
    }
}
