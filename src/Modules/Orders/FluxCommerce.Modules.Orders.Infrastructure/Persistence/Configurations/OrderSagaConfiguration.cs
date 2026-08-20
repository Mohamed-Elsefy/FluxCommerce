using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Configurations;

/// <summary>
/// Phase 8.2 maps the <see cref="OrderSaga"/> (ADR-009) into the Orders DbContext. One saga per order
/// is enforced with a unique index on <c>OrderId</c>; the <c>CorrelationId</c> lookup of the consumers
/// (Phase 8.5) is covered by a non-unique index. <c>Version</c> is the optimistic concurrency token, so
/// the last writer wins and conflicting concurrent result events cannot both be applied.
///
/// <para>
/// Phase 8.7 maps the saga's recorded reservations (<see cref="OrderSaga.ReservationIds"/>) as an EF
/// Core primitive collection backed by the private <c>_reservationIds</c> field: the identifiers are
/// stored as a JSON array in a single <c>ReservationIds</c> column, so the saga stays one row with no
/// extra table. The public read-only projection is ignored to keep the field as the mapped property.
/// </para>
/// </summary>
public sealed class OrderSagaConfiguration : IEntityTypeConfiguration<OrderSaga>
{
    public void Configure(EntityTypeBuilder<OrderSaga> builder)
    {
        builder.ToTable("OrderSagas");

        builder.HasKey(saga => saga.Id);

        builder.Property(saga => saga.Id)
            .HasColumnName("Id");

        builder.Property(saga => saga.OrderId)
            .HasColumnName("OrderId")
            .HasConversion(orderId => orderId.Value, value => new OrderId(value));

        builder.Property(saga => saga.CorrelationId)
            .HasColumnName("CorrelationId");

        builder.Property(saga => saga.State)
            .HasColumnName("State")
            .HasConversion<int>();

        builder.Property(saga => saga.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc")
            .HasColumnType("datetimeoffset");

        builder.Property(saga => saga.UpdatedAtUtc)
            .HasColumnName("UpdatedAtUtc")
            .HasColumnType("datetimeoffset");

        builder.Property(saga => saga.Version)
            .HasColumnName("Version")
            .IsConcurrencyToken();

        builder.Ignore(saga => saga.ReservationIds);

        builder.Property(typeof(List<Guid>), "_reservationIds")
            .HasColumnName("ReservationIds");

        builder.HasIndex(saga => saga.OrderId)
            .IsUnique()
            .HasDatabaseName("UX_OrderSagas_OrderId");

        builder.HasIndex(saga => saga.CorrelationId)
            .HasDatabaseName("IX_OrderSagas_CorrelationId");
    }
}