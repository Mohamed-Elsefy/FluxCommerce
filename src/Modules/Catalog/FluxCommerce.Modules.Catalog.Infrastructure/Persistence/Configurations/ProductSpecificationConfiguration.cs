using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class ProductSpecificationConfiguration : IEntityTypeConfiguration<ProductSpecification>
{
    public void Configure(EntityTypeBuilder<ProductSpecification> builder)
    {
        builder.ToTable("ProductSpecifications");

        builder.HasKey(specification => specification.Id);

        builder.Property(specification => specification.Id).ValueGeneratedNever();

        builder.Property(specification => specification.Name)
            .HasColumnName("Name")
            .HasMaxLength(ProductSpecification.NameMaxLength);

        builder.Property(specification => specification.Value)
            .HasColumnName("Value")
            .HasMaxLength(ProductSpecification.ValueMaxLength);

        builder.Property(specification => specification.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.HasIndex(specification => specification.Name);
    }
}
