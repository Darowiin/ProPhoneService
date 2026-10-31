namespace ProPhoneService.Application.Services;

/// <summary>
/// Пагинация списков заказов: панель мастера и личный кабинет клиента
/// </summary>
public static class OrderPaging
{
    /// <summary>
    /// Размер страницы
    /// </summary>
    public const int PageSize = 25;

    /// <summary>
    /// Максимальный номер страницы, при котором смещение не переполняет int
    /// </summary>
    public static readonly int MaxPage = int.MaxValue / PageSize + 1;
}
