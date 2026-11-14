namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Ответ на создание заявки: номер (идентификатор) заявки
/// </summary>
/// <param name="Id">Номер заявки</param>
/// <param name="RequiresContactConfirmation">Заявка неактивна: подтвердите контакт кодом из VK</param>
/// <param name="MessengerLink">Ссылка привязки VK для гостевой заявки</param>
public record CreateOrderResponse(Guid Id, bool RequiresContactConfirmation, MessengerLinkResponse? MessengerLink = null)
{
    /// <summary>
    /// Постоянный номер заявки
    /// </summary>
    public string OrderNumber { get; init; } = "";
}
