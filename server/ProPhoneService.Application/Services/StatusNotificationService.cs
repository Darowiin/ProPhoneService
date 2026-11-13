using Microsoft.Extensions.Logging;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Уведомление клиента о смене статуса заказа: повторы с backoff при сбое провайдера
/// и запись в <c>notification_log</c> (кому, когда, по какому заказу, статус доставки)
/// </summary>
public class StatusNotificationService(
    INotifier notifier,
    IRepository<NotificationLog> notificationRepository,
    NotificationOptions notificationOptions,
    ILogger<StatusNotificationService> logger)
{
    /// <summary>
    /// Отправить клиенту уведомление о текущем статусе заказа
    /// </summary>
    /// <remarks>Получатель - телефон заявки или клиента; без получателя уведомление не создаётся</remarks>
    public async Task NotifyStatusChangedAsync(
        RepairOrder order,
        CancellationToken cancellationToken = default)
    {
        var recipient = FirstNonEmpty(order.ContactPhone, order.Client?.Phone);
        if (recipient is null)
        {
            logger.LogWarning("Заказ {OrderId}: нет телефон для уведомления о смене статуса", order.Id);
            return;
        }

        var subject = $"Статус заказа: {order.Status}";
        var message = BuildMessage(order);
        var maxAttempts = Math.Max(1, notificationOptions.MaxAttempts);
        var attempts = 0;
        var status = NotificationDeliveryStatus.Failed;

        while (attempts < maxAttempts)
        {
            attempts++;

            try
            {
                await notifier.SendAsync(recipient, subject, message, cancellationToken);
                status = NotificationDeliveryStatus.Sent;
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Заказ {OrderId}: попытка {Attempt} из {MaxAttempts} не удалась ({Recipient})",
                    order.Id,
                    attempts,
                    maxAttempts,
                    recipient);

                if (attempts < maxAttempts)
                {
                    // Backoff: пауза удваивается с каждой следующей попыткой
                    var delayMilliseconds = notificationOptions.RetryDelay.TotalMilliseconds * Math.Pow(2, attempts - 1);
                    if (delayMilliseconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), cancellationToken);
                    }
                }
            }
        }

        await notificationRepository.AddAsync(new NotificationLog
        {
            Id = Guid.NewGuid(),
            RepairOrderId = order.Id,
            Recipient = recipient,
            Subject = subject,
            Message = message,
            Status = status,
            Attempts = attempts,
        }, cancellationToken);

        await notificationRepository.SaveChangesAsync(cancellationToken);

        if (status == NotificationDeliveryStatus.Failed)
        {
            logger.LogError(
                "Заказ {OrderId}: уведомление не доставлено после {Attempts} попыток ({Recipient})",
                order.Id,
                attempts,
                recipient);
        }
    }

    private static string BuildMessage(RepairOrder order)
    {
        var comment = order.StatusHistory.Count > 0 ? order.StatusHistory[^1].Comment : null;

        return string.IsNullOrWhiteSpace(comment)
            ? $"Заказ {order.Id}: новый статус - {order.Status}"
            : $"Заказ {order.Id}: новый статус - {order.Status}. Комментарий: {comment}";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
