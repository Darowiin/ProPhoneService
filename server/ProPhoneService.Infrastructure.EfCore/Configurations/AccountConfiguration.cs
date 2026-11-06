using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Конфигурация сущности <see cref="Account"/>
/// </summary>
public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        // Один аккаунт на одного клиента
        builder.HasIndex(x => x.ClientId).IsUnique();
    }
}
