using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Конфигурация сущности <see cref="AuthCode"/>
/// </summary>
public class AuthCodeConfiguration : IEntityTypeConfiguration<AuthCode>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuthCode> builder)
    {
        // Один активный код на email
        builder.HasIndex(x => x.Email).IsUnique();
    }
}
