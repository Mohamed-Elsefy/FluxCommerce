using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> builder)
    {
        builder.ToTable("AttributeDefinitions");

        builder.HasKey(attributeDefinition => attributeDefinition.Id);

        builder.Property(attributeDefinition => attributeDefinition.Name)
            .HasColumnName("Name")
            .HasMaxLength(AttributeDefinition.NameMaxLength);

        builder.Property(attributeDefinition => attributeDefinition.Description)
            .HasColumnName("Description")
            .HasMaxLength(AttributeDefinition.DescriptionMaxLength);

        builder.Property(attributeDefinition => attributeDefinition.DataType)
            .HasColumnName("DataType");

        builder.Property(attributeDefinition => attributeDefinition.IsRequired)
            .HasColumnName("IsRequired");

        builder.Property(attributeDefinition => attributeDefinition.IsFilterable)
            .HasColumnName("IsFilterable");

        builder.Property(attributeDefinition => attributeDefinition.IsSearchable)
            .HasColumnName("IsSearchable");

        builder.Property(attributeDefinition => attributeDefinition.SortOrder)
            .HasColumnName("SortOrder");

        builder.Property(attributeDefinition => attributeDefinition.CategoryId)
            .HasColumnName("CategoryId");

        builder.Property(attributeDefinition => attributeDefinition.Status)
            .HasColumnName("Status");

        builder.Property(attributeDefinition => attributeDefinition.IsDeleted)
            .HasColumnName("IsDeleted");

        builder.Property(attributeDefinition => attributeDefinition.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(attributeDefinition => attributeDefinition.DeletedAtUtc)
            .HasColumnName("DeletedAtUtc");

        builder.OwnsOne(attributeDefinition => attributeDefinition.Slug, slug =>
        {
            slug.Property(s => s.Value)
                .HasColumnName("Slug")
                .HasMaxLength(Slug.MaxLength);

            slug.HasIndex(s => s.Value)
                .IsUnique();
        });

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(attributeDefinition => attributeDefinition.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(attributeDefinition => attributeDefinition.CategoryId);

        builder.HasIndex(attributeDefinition => attributeDefinition.Name);
    }
}
