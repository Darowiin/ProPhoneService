namespace ProPhoneService.Domain.Shared.Enum;

/// <summary>
/// Роль аккаунта: клиент видит только свои заказы, мастер - панель мастерской
/// </summary>
public enum AccountRole
{
    /// <summary>
    /// Клиент мастерской: ЛК, свои заказы и отзывы
    /// </summary>
    Client,

    /// <summary>
    /// Мастер: панель заказов, смена статусов, правка прайса
    /// </summary>
    Master,
}
