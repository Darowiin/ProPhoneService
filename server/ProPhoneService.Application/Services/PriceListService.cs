using MapsterMapper;
using Microsoft.Extensions.Caching.Hybrid;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Прайс-лист мастерской: чтение активных услуг с кэшированием
/// </summary>
public class PriceListService(
    IServiceRepository serviceRepository,
    IMapper mapper,
    HybridCache cache)
{
    /// <summary>
    /// Ключ кэша списка услуг
    /// </summary>
    public const string CacheKey = "services:active";

    /// <summary>
    /// Тег для инвалидации кэша прайс-листа (задел на правку прайса в спринте 2)
    /// </summary>
    public const string CacheTag = "services";

    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5),
    };

    /// <summary>
    /// Получить активные услуги прайс-листа (из кэша, при промахе - из БД)
    /// </summary>
    public async Task<IReadOnlyList<ServiceDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            CacheKey,
            async token => await LoadAsync(token),
            _cacheOptions,
            [CacheTag],
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Сбросить кэш прайс-листа (вызывается после правки услуг)
    /// </summary>
    public ValueTask InvalidateAsync(CancellationToken cancellationToken = default)
    {
        return cache.RemoveByTagAsync(CacheTag, cancellationToken);
    }

    /// <summary>
    /// Создать услугу прайс-листа (Для мастера)
    /// </summary>
    public async Task<ServiceDto> CreateAsync(
        ServiceUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var service = new Service
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            IsActive = true,
        };

        await serviceRepository.AddAsync(service, cancellationToken);
        await serviceRepository.SaveChangesAsync(cancellationToken);

        return mapper.Map<ServiceDto>(service);
    }

    /// <summary>
    /// Обновить услугу прайс-листа (Для мастера)
    /// </summary>
    /// <returns>Обновлённая услуга или <c>null</c>, если не найдена</returns>
    public async Task<ServiceDto?> UpdateAsync(
        Guid id,
        ServiceUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var service = await serviceRepository.GetByIdAsync(id, cancellationToken);
        if (service is null)
        {
            return null;
        }

        service.Name = request.Name;
        service.Description = request.Description;
        service.Price = request.Price;
        serviceRepository.Update(service);

        await serviceRepository.SaveChangesAsync(cancellationToken);

        return mapper.Map<ServiceDto>(service);
    }

    /// <summary>
    /// Скрыть услугу из прайс-листа (Для мастера). Реальное удаление не применяется - скрытие
    /// </summary>
    /// <returns><c>true</c>, если услуга найдена и скрыта; <c>false</c>, если не найдена</returns>
    public async Task<bool> HideAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var service = await serviceRepository.GetByIdAsync(id, cancellationToken);
        if (service is null)
        {
            return false;
        }

        service.IsActive = false;
        serviceRepository.Update(service);

        await serviceRepository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<ServiceDto[]> LoadAsync(CancellationToken cancellationToken)
    {
        var services = await serviceRepository.GetAllActiveAsync(cancellationToken);

        return mapper.Map<ServiceDto[]>(services);
    }
}
