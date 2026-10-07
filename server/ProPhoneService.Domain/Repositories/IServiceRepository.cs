using ProPhoneService.Domain.Model;

namespace ProPhoneService.Domain.Repositories;

/// <summary>
/// Репозиторий услуг из прайс-листа
/// </summary>
public interface IServiceRepository : IRepository<Service>
{
    /// <summary>
    /// Получить все активные услуги, отсортированные по названию
    /// </summary>
    /// <remarks>Для витрины и формы заявки</remarks>
    Task<IReadOnlyList<Service>> GetAllActiveAsync(CancellationToken cancellationToken = default);
}
