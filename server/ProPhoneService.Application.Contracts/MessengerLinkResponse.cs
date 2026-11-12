using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Ссылка для открытия бота, токен в ссылке действует однократно
/// </summary>
/// <param name="Channel">Выбранный мессенджер</param>
/// <param name="Url">Ссылка с одноразовым токеном</param>
/// <param name="ExpiresAtUtc">Предельное время привязки</param>
public record MessengerLinkResponse(MessengerChannel Channel, string Url, DateTime ExpiresAtUtc);
