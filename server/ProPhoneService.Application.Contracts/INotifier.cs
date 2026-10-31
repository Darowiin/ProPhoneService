namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Отправка сообщений клиенту (email/SMS, реализация - Infrastructure, провайдер - Карточка 5)
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Отправить сообщение по указанному контакту
    /// </summary>
    /// <param name="target">Телефон или email получателя</param>
    /// <param name="text">Текст сообщения</param>
    Task SendAsync(string target, string text, CancellationToken cancellationToken = default);
}
