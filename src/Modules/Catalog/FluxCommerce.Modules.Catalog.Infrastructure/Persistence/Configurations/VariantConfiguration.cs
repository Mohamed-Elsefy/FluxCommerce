using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Catalog.Infrastructure.Persistence.Configurations;

public sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> builder)
    {
        builder.ToTable("Variants");

        builder.HasKey(variant => variant.Id);

        builder.Property(variant => variant.ProductId)
            .HasColumnName("ProductId");

        builder.Property(variant => variant.Name)
            .HasColumnName("Name")
            .HasMaxLength(Variant.NameMaxLength);

        builder.Property(variant => variant.Status)
            .HasColumnName("Status");

        builder.Property(variant => variant.IsDeleted)
            .HasColumnName("IsDeleted");

        builder.Property(variant => variant.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(variant => variant.DeletedAtUtc)
            .HasColumnName("DeletedAtUtc");

        builder.OwnsOne(variant => variant.Sku, sku =>
        {
            sku.Property(s => s.Value)
                .HasColumnName("Sku")
                .HasMaxLength(Sku.MaxLength);

            sku.HasIndex(s => s.Value)
                .IsUnique();
        });

        builder.OwnsOne(variant => variant.UnitPrice, unitPrice =>
        {
            unitPrice.Property(p => p.Amount)
                .HasColumnName("UnitPriceAmount")
                .HasPrecision(18, 6);

            unitPrice.Property(p => p.Currency)
                .HasColumnName("UnitPriceCurrency")
                .HasMaxLength(3);
        });

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(variant => variant.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(variant => variant.ProductId);
    }
}
