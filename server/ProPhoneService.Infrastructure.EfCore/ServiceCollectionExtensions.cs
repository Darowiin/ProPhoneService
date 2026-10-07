using Microsoft.Extensions.DependencyInjection;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Infrastructure.EfCore.Repositories;

namespace ProPhoneService.Infrastructure.EfCore;

/// <summary>
/// Регистрация репозиториев в DI-контейнере
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Добавить репозитории
    /// </summary>
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IRepairOrderRepository, RepairOrderRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();

        return services;
    }
}
