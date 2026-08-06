using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class ProductAttributeValueConfiguration : IEntityTypeConfiguration<ProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProductAttributeValue> builder)
    {
        builder.ToTable("ProductAttributeValues");

        builder.HasKey(attributeValue => attributeValue.Id);

        builder.Property(attributeValue => attributeValue.Id).ValueGeneratedNever();

        builder.Property(attributeValue => attributeValue.AttributeDefinitionId)
            .HasColumnName("AttributeDefinitionId");

        builder.Property(attributeValue => attributeValue.Value)
            .HasColumnName("Value")
            .HasMaxLength(ProductAttributeValue.ValueMaxLength);

        builder.Property(attributeValue => attributeValue.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.HasOne<AttributeDefinition>()
            .WithMany()
            .HasForeignKey(attributeValue => attributeValue.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(attributeValue => attributeValue.AttributeDefinitionId);
    }
}
