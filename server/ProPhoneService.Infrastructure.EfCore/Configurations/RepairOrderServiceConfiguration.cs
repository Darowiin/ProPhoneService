using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Конфигурация сущности-связки <see cref="RepairOrderService"/>
/// </summary>
public class RepairOrderServiceConfiguration : IEntityTypeConfiguration<RepairOrderService>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RepairOrderService> builder)
    {
        builder.HasKey(x => new { x.RepairOrderId, x.ServiceId });
    }
}
