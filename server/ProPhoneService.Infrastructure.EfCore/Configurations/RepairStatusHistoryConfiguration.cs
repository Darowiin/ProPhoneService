using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Конфигурация сущности <see cref="RepairStatusHistory"/>
/// </summary>
public class RepairStatusHistoryConfiguration : IEntityTypeConfiguration<RepairStatusHistory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RepairStatusHistory> builder)
    {
        builder.Property(x => x.Status).HasConversion<string>();

        builder.HasIndex(x => x.RepairOrderId);
        builder.HasIndex(x => x.ChangedAt);
    }
}
