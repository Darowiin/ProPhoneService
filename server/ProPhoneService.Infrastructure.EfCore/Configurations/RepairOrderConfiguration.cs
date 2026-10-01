using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Конфигурация сущности <see cref="RepairOrder"/>
/// </summary>
public class RepairOrderConfiguration : IEntityTypeConfiguration<RepairOrder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RepairOrder> builder)
    {
        builder.Property(x => x.Status).HasConversion<string>();

        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.DeviceId);
        builder.HasIndex(x => x.Status);

        builder
            .HasMany(x => x.StatusHistory)
            .WithOne(x => x.RepairOrder)
            .HasForeignKey(x => x.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(x => x.Services)
            .WithOne(x => x.RepairOrder)
            .HasForeignKey(x => x.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
