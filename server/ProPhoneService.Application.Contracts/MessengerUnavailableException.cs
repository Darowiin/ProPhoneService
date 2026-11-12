using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Ошибка доставки или отсутствующая настройка мессенджера без секретов провайдера
/// </summary>
/// <param name="channel">Канал доставки</param>
public class MessengerUnavailableException(MessengerChannel? channel = null)
    : Exception("Мессенджер временно недоступен. Повторите попытку позже или оформите заказ лично в мастерской")
{
    /// <summary>
    /// Канал, в котором произошла ошибка
    /// </summary>
    public MessengerChannel? Channel { get; } = channel;
}
