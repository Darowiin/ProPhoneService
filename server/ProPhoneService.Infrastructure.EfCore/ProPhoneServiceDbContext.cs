using Microsoft.EntityFrameworkCore;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Infrastructure.EfCore;

/// <summary>
/// EF Core контекст базы данных ProPhoneService
/// </summary>
public class ProPhoneServiceDbContext(DbContextOptions<ProPhoneServiceDbContext> options)
    : DbContext(options)
{
    /// <summary>
    /// Клиенты мастерской
    /// </summary>
    public DbSet<Client> Clients => Set<Client>();

    /// <summary>
    /// Устройства
    /// </summary>
    public DbSet<Device> Devices => Set<Device>();

    /// <summary>
    /// Заказы на ремонт
    /// </summary>
    public DbSet<RepairOrder> RepairOrders => Set<RepairOrder>();

    /// <summary>
    /// История смены статусов заказов
    /// </summary>
    public DbSet<RepairStatusHistory> RepairStatusHistories => Set<RepairStatusHistory>();

    /// <summary>
    /// Услуги из прайс-листа
    /// </summary>
    public DbSet<Service> Services => Set<Service>();

    /// <summary>
    /// Отзывы клиентов
    /// </summary>
    public DbSet<Review> Reviews => Set<Review>();

    /// <summary>
    /// Связи заказ — услуга
    /// </summary>
    public DbSet<RepairOrderService> RepairOrderServices => Set<RepairOrderService>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProPhoneServiceDbContext).Assembly);
    }
}
