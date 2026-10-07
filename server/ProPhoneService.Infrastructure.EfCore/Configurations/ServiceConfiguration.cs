using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Конфигурация сущности <see cref="Service"/>
/// </summary>
public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.HasIndex(x => x.IsActive);

        builder
            .HasMany(x => x.RepairOrders)
            .WithOne(x => x.Service)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
