using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Конфигурация сущности <see cref="NotificationLog"/>
/// </summary>
public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.Property(x => x.Status).HasConversion<string>();
        builder.Property(x => x.Channel).HasConversion<string>();

        builder.HasIndex(x => x.RepairOrderId);
        builder.HasIndex(x => x.CreatedAt);

        builder
            .HasOne(x => x.RepairOrder)
            .WithMany()
            .HasForeignKey(x => x.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
