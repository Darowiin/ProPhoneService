using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Infrastructure.EfCore.Configurations;

/// <summary>
/// Конфигурация сущности <see cref="Review"/>
/// </summary>
public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable(x => x.HasCheckConstraint("CK_review_rating_range", "\"rating\" BETWEEN 1 AND 5"));

        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.CreatedAt);
    }
}
