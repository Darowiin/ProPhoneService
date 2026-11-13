namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Профиль текущего клиента и состояние привязки VK
/// </summary>
/// <param name="Id">Идентификатор клиента</param>
/// <param name="Name">Имя клиента</param>
/// <param name="Phone">Телефон клиента</param>
/// <param name="VkLinked">VK привязан к текущему аккаунту</param>
public record MeProfileDto(Guid Id, string Name, string? Phone, bool VkLinked);
