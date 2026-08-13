using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Configurations;

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("InventoryReservations", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryReservations_Quantity_Positive",
                "[Quantity] > 0");
        });

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.Id)
            .HasColumnName("Id")
            .HasConversion(id => id.Value, value => new InventoryReservationId(value));

        builder.Property(reservation => reservation.OrderId)
            .HasColumnName("OrderId")
            .HasConversion(orderId => orderId.Value, value => new OrderId(value));

        builder.Property(reservation => reservation.Sku)
            .HasColumnName("Sku")
            .HasMaxLength(InventorySku.MaxLength)
            .HasConversion(sku => sku.Value, value => new InventorySku(value));

        builder.Property(reservation => reservation.Quantity)
            .HasColumnName("Quantity");

        builder.Property(reservation => reservation.Status)
            .HasColumnName("Status");

        builder.Property(reservation => reservation.ExpiresAt)
            .HasColumnName("ExpiresAt");

        builder.Property(reservation => reservation.Version)
            .HasColumnName("Version")
            .IsConcurrencyToken();

        builder.Property(reservation => reservation.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(reservation => reservation.UpdatedAtUtc)
            .HasColumnName("UpdatedAtUtc");

        builder.HasIndex(reservation => reservation.Sku)
            .HasDatabaseName("IX_InventoryReservations_Sku");

        builder.HasIndex(reservation => reservation.OrderId)
            .HasDatabaseName("IX_InventoryReservations_OrderId");

        builder.HasIndex(reservation => new { reservation.Status, reservation.ExpiresAt })
            .HasDatabaseName("IX_InventoryReservations_Status_ExpiresAt");
    }
}
