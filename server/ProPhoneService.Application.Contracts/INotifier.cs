using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Отправка кодов и уведомлений через VK
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Отправить текст получателю выбранного мессенджера
    /// </summary>
    /// <param name="channel">Выбранный канал</param>
    /// <param name="recipientId">Идентификатор пользователя VK</param>
    /// <param name="text">Текст сообщения</param>
    /// <param name="cancellationToken">Отмена операции</param>
    Task SendAsync(MessengerChannel channel, string recipientId, string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отправить сообщение через привязанный мессенджер по телефону контакта
    /// </summary>
    /// <param name="target">Телефон контакта</param>
    /// <param name="subject">Заголовок сообщения</param>
    /// <param name="text">Текст сообщения</param>
    /// <param name="cancellationToken">Отмена операции</param>
    Task SendAsync(string target, string subject, string text, CancellationToken cancellationToken = default);
}
