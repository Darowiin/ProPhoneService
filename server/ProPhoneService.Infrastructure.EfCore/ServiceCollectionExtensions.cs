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

        return services;
    }

    /// <summary>
    /// Регистрация отправителя сообщений: SMTP-провайдер (Resend) или лог-заглушка
    /// </summary>
    /// <param name="services">Коллекция сервисов</param>
    /// <param name="useEmailProvider"><c>true</c> - письма через SMTP, <c>false</c> - запись в лог (демо)</param>
    public static IServiceCollection AddNotifier(this IServiceCollection services, bool useEmailProvider)
    {
        if (useEmailProvider)
        {
            services.AddScoped<INotifier, SmtpEmailNotifier>();
        }
        else
        {
            services.AddScoped<INotifier, LoggingNotifier>();
        }

        return services;
    }
}
