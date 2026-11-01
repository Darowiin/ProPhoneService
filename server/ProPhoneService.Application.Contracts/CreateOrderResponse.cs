namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Ответ на создание заявки: номер (идентификатор) заявки
/// </summary>
/// <param name="Id">Номер заявки</param>
/// <param name="RequiresEmailConfirmation">Заявка неактивна: подтвердите email кодом из письма</param>
public record CreateOrderResponse(Guid Id, bool RequiresEmailConfirmation);
