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
    /// Признак неподтверждённой заявки: требуется код на email, чтобы она стала активной
    /// </summary>
    public bool RequiresEmailConfirmation { get; private init; }

    /// <summary>
    /// Признак успешного создания
    /// </summary>
    public bool IsSuccess => OrderId is not null;

    /// <summary>
    /// Успешный результат с номером заявки
    /// </summary>
    /// <param name="orderId">Номер созданной заявки</param>
    /// <param name="requiresEmailConfirmation">Заявка ждёт подтверждения email (гостевая)</param>
    public static CreateOrderResult Success(Guid orderId, bool requiresEmailConfirmation = false) =>
        new() { OrderId = orderId, RequiresEmailConfirmation = requiresEmailConfirmation };

    /// <summary>
    /// Неудача с ошибками по полям
    /// </summary>
    public static CreateOrderResult Failure(IReadOnlyDictionary<string, string[]> errors) => new() { Errors = errors };
}
