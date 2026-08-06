using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name)
            .HasColumnName("Name")
            .HasMaxLength(Category.NameMaxLength);

        builder.Property(category => category.Description)
            .HasColumnName("Description")
            .HasMaxLength(Category.DescriptionMaxLength);

        builder.Property(category => category.ParentId)
            .HasColumnName("ParentId");

        builder.Property(category => category.Status)
            .HasColumnName("Status");

        builder.Property(category => category.IsDeleted)
            .HasColumnName("IsDeleted");

        builder.Property(category => category.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(category => category.DeletedAtUtc)
            .HasColumnName("DeletedAtUtc");

        builder.OwnsOne(category => category.Slug, slug =>
        {
            slug.Property(s => s.Value)
                .HasColumnName("Slug")
                .HasMaxLength(Slug.MaxLength);

            slug.HasIndex(s => s.Value)
                .IsUnique();
        });

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(category => category.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(category => category.ParentId);

        builder.HasIndex(category => category.Name);
    }
}
