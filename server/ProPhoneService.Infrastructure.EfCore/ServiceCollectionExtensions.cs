using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Infrastructure.EfCore.Auditing;
using ProPhoneService.Infrastructure.EfCore.Notifications;
using ProPhoneService.Infrastructure.EfCore.Repositories;

namespace ProPhoneService.Infrastructure.EfCore;

/// <summary>
/// Регистрация репозиториев в DI-контейнере
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Добавить репозитории, контекст базы данных и аудит изменений
    /// </summary>
    /// <param name="services">Коллекция сервисов</param>
    /// <param name="connectionString">Строка подключения; без неё контекст настраивается отдельно (тесты)</param>
    public static IServiceCollection AddRepositories(
        this IServiceCollection services,
        string? connectionString = null)
    {
        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<ProPhoneServiceDbContext>((sp, options) =>
        {
            if (connectionString is not null)
            {
                options.UseNpgsql(connectionString);
            }
            options.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
        });

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IRepairOrderRepository, RepairOrderRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAuthCodeRepository, AuthCodeRepository>();
        services.AddScoped<IMessengerBindingRepository, MessengerBindingRepository>();

        return services;
    }

    /// <summary>
    /// Зарегистрировать отправителей Telegram и VK с ограниченным временем ожидания
    /// </summary>
    public static IServiceCollection AddNotifier(this IServiceCollection services)
    {
        services.AddHttpClient<TelegramBotNotifier>(client =>
        {
            client.BaseAddress = new Uri("https://api.telegram.org/");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).RemoveAllLoggers();

        services.AddHttpClient<VkNotifier>(client =>
        {
            client.BaseAddress = new Uri("https://api.vk.com/method/");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).RemoveAllLoggers();

        services.AddScoped<INotifier, MessengerNotifier>();

        return services;
    }
}
