using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Хранение привязок VK
/// </summary>
public class MessengerBindingConfiguration : IEntityTypeConfiguration<MessengerBinding>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MessengerBinding> builder)
    {
        builder.Property(x => x.Channel).HasConversion<string>();
        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.Property(x => x.Phone).HasMaxLength(12);
        builder.Property(x => x.RecipientId).HasMaxLength(32);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.Phone, x.LinkedAt });
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.AccountId);
        builder.HasOne<RepairOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_messenger_binding_owner",
            "(order_id IS NOT NULL AND account_id IS NULL) OR (order_id IS NULL AND account_id IS NOT NULL)"));
    }
}
