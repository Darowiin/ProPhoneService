using ProPhoneService.Application.Contracts;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Результат создания заявки: либо номер созданной заявки, либо ошибки валидации по полям
/// </summary>
public class CreateOrderResult
{
    private CreateOrderResult()
    {
    }

    /// <summary>
    /// Номер созданной заявки (заполнен при успехе)
    /// </summary>
    public Guid? OrderId { get; private init; }

    /// <summary>
    /// Ошибки валидации: ключ — имя поля, значение — сообщения (заполнены при неудаче)
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; private init; }

    /// <summary>
    /// Признак неподтверждённой заявки: требуется код из VK, чтобы она стала активной
    /// </summary>
    public bool RequiresContactConfirmation { get; private init; }

    /// <summary>
    /// Ссылка для перехода в VK и срок её действия
    /// </summary>
    public MessengerLinkResponse? MessengerLink { get; private init; }

    /// <summary>
    /// Признак успешного создания
    /// </summary>
    public bool IsSuccess => OrderId is not null;

    /// <summary>
    /// Успешный результат с номером заявки
    /// </summary>
    /// <param name="orderId">Номер созданной заявки</param>
    /// <param name="requiresContactConfirmation">Заявка ждёт подтверждения контакта (гостевая)</param>
    public static CreateOrderResult Success(Guid orderId, bool requiresContactConfirmation = false, MessengerLinkResponse? messengerLink = null) =>
        new() { OrderId = orderId, RequiresContactConfirmation = requiresContactConfirmation, MessengerLink = messengerLink };

    /// <summary>
    /// Неудача с ошибками по полям
    /// </summary>
    public static CreateOrderResult Failure(IReadOnlyDictionary<string, string[]> errors) => new() { Errors = errors };
}
