namespace ProPhoneService.Domain.Shared.Auditing;

/// <summary>
/// Текущий авторизованный аккаунт запроса: источник для полей аудита "кто изменил"
/// </summary>
public interface ICurrentActor
{
    /// <summary>
    /// Идентификатор аккаунта из токена или <c>null</c> для неавторизованного запроса
    /// </summary>
    Guid? AccountId { get; }
}
