using System.Text.Json;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages");

        builder.HasKey(image => image.Id);

        builder.Property(image => image.Id).ValueGeneratedNever();

        builder.Property(image => image.ImageUrl)
            .HasColumnName("ImageUrl")
            .HasMaxLength(ProductImage.ImageUrlMaxLength);

        builder.Property(image => image.AltText)
            .HasColumnName("AltText")
            .HasMaxLength(ProductImage.AltTextMaxLength);

        builder.Property(image => image.DisplayOrder)
            .HasColumnName("DisplayOrder");

        builder.Property(image => image.IsPrimary)
            .HasColumnName("IsPrimary");

        builder.Property(image => image.Metadata)
            .HasColumnName("Metadata")
            .HasConversion(new ImageMetadataConverter());

        builder.Property(image => image.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.HasIndex(image => image.DisplayOrder);
    }

    private sealed class ImageMetadataConverter : ValueConverter<Dictionary<string, string>?, string?>
    {
        public ImageMetadataConverter()
            : base(
                metadata => ToProvider(metadata),
                json => FromProvider(json))
        {
        }

        private static string? ToProvider(Dictionary<string, string>? metadata) =>
            metadata is null ? null : JsonSerializer.Serialize(metadata);

        private static Dictionary<string, string>? FromProvider(string? json) =>
            json is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }
}
