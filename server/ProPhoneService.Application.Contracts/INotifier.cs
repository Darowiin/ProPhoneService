namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Отправка сообщений клиенту (email/SMS); каналы добавляются реализациями в Infrastructure
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Отправить сообщение по указанному контакту
    /// </summary>
    /// <param name="target">Телефон или email получателя</param>
    /// <param name="subject">Тема сообщения (для email)</param>
    /// <param name="text">Текст сообщения</param>
    Task SendAsync(string target, string subject, string text, CancellationToken cancellationToken = default);
}
