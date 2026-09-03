using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Configurations;

/// <summary>
/// Phase 7.9: maps the <see cref="OrderSummaryProjectionEntity"/> (the durable Order Summary read
/// model) into the Orders DbContext. The order id is the primary key — the projection is maintained
/// by an idempotent consumer, so a single order has exactly one projection row. The
/// <c>(CustomerId, CreatedAtUtc)</c> index serves the customer-facing read side that will consume
/// this model; no speculative indexes are added.
/// </summary>
public sealed class OrderSummaryProjectionConfiguration : IEntityTypeConfiguration<OrderSummaryProjectionEntity>
{
    public void Configure(EntityTypeBuilder<OrderSummaryProjectionEntity> builder)
    {
        builder.ToTable("OrderSummaryProjections");

        builder.HasKey(entity => entity.OrderId);

        builder.Property(entity => entity.OrderId)
            .HasColumnName("OrderId");

        builder.Property(entity => entity.OrderNumber)
            .HasColumnName("OrderNumber")
            .HasMaxLength(OrderSummaryProjectionEntity.OrderNumberMaxLength);

        builder.Property(entity => entity.CustomerId)
            .HasColumnName("CustomerId");

        builder.Property(entity => entity.Status)
            .HasColumnName("Status")
            .HasMaxLength(OrderSummaryProjectionEntity.StatusMaxLength);

        builder.Property(entity => entity.GrandTotalAmount)
            .HasColumnName("GrandTotalAmount");

        builder.Property(entity => entity.GrandTotalCurrency)
            .HasColumnName("GrandTotalCurrency")
            .HasMaxLength(OrderSummaryProjectionEntity.GrandTotalCurrencyMaxLength);

        builder.Property(entity => entity.ItemCount)
            .HasColumnName("ItemCount");

        builder.Property(entity => entity.CorrelationId)
            .HasColumnName("CorrelationId");

        builder.Property(entity => entity.CausationId)
            .HasColumnName("CausationId");

        builder.Property(entity => entity.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(entity => entity.ProjectedAtUtc)
            .HasColumnName("ProjectedAtUtc");

        builder.HasIndex(entity => new { entity.CustomerId, entity.CreatedAtUtc })
            .HasDatabaseName("IX_OrderSummaryProjections_CustomerId_CreatedAtUtc");
    }
}