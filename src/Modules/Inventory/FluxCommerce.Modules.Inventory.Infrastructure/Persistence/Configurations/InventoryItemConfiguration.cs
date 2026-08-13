using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Configurations;

public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryItems_OnHandQuantity_NonNegative",
                "[OnHandQuantity] >= 0");

            table.HasCheckConstraint(
                "CK_InventoryItems_ReservedQuantity_NonNegative",
                "[ReservedQuantity] >= 0");

            table.HasCheckConstraint(
                "CK_InventoryItems_Reserved_NotExceeds_OnHand",
                "[ReservedQuantity] <= [OnHandQuantity]");
        });

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("Sku")
            .HasMaxLength(InventorySku.MaxLength)
            .HasConversion(sku => sku.Value, value => new InventorySku(value));

        builder.Property(item => item.OnHandQuantity)
            .HasColumnName("OnHandQuantity");

        builder.Property(item => item.ReservedQuantity)
            .HasColumnName("ReservedQuantity");

        builder.Ignore(item => item.AvailableQuantity);

        builder.Property(item => item.Version)
            .HasColumnName("Version")
            .IsConcurrencyToken();

        builder.Property(item => item.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(item => item.UpdatedAtUtc)
            .HasColumnName("UpdatedAtUtc");
    }
}
