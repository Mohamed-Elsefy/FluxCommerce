using System.Text.Json;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(product => product.Id);

        builder.Property(product => product.Name)
            .HasColumnName("Name")
            .HasMaxLength(Product.NameMaxLength);

        builder.Property(product => product.Description)
            .HasColumnName("Description")
            .HasMaxLength(Product.DescriptionMaxLength);

        builder.Property(product => product.CategoryId)
            .HasColumnName("CategoryId");

        builder.Property(product => product.Status)
            .HasColumnName("Status");

        builder.Property(product => product.Visibility)
            .HasColumnName("Visibility");

        builder.Property(product => product.IsDeleted)
            .HasColumnName("IsDeleted");

        builder.Property(product => product.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(product => product.DeletedAtUtc)
            .HasColumnName("DeletedAtUtc");

        builder.OwnsOne(product => product.Slug, slug =>
        {
            slug.Property(s => s.Value)
                .HasColumnName("Slug")
                .HasMaxLength(Slug.MaxLength);

            slug.HasIndex(s => s.Value)
                .IsUnique();
        });

        builder.Property(product => product.Seo)
            .HasColumnName("Seo")
            .HasConversion(new SeoMetadataConverter());

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(product => product.AttributeValues)
            .WithOne()
            .HasForeignKey("ProductId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(product => product.AttributeValues)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(product => product.CategoryId);

        builder.HasIndex(product => product.Name);
    }

    private sealed class SeoMetadataConverter : ValueConverter<SeoMetadata?, string?>
    {
        public SeoMetadataConverter()
            : base(
                seo => ToProvider(seo),
                json => FromProvider(json))
        {
        }

        private static string? ToProvider(SeoMetadata? seo) =>
            seo is null ? null : JsonSerializer.Serialize(SeoMetadataDto.From(seo));

        private static SeoMetadata? FromProvider(string? json) =>
            json is null ? null : FromDto(JsonSerializer.Deserialize<SeoMetadataDto>(json));

        private static SeoMetadata FromDto(SeoMetadataDto? dto) =>
            dto is null
                ? throw new InvalidOperationException("Stored SEO metadata could not be deserialized.")
                : SeoMetadata.Create(dto.Title, dto.Description, dto.Keywords);
    }

    private sealed record SeoMetadataDto(string? Title, string? Description, List<string>? Keywords)
    {
        public static SeoMetadataDto From(SeoMetadata seo) =>
            new(seo.Title, seo.Description, seo.Keywords.ToList());
    }
}
