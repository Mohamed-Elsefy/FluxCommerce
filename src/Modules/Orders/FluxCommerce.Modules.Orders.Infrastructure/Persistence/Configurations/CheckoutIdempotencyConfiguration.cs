using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Configurations;

public sealed class CheckoutIdempotencyConfiguration : IEntityTypeConfiguration<CheckoutIdempotency>
{
    public void Configure(EntityTypeBuilder<CheckoutIdempotency> builder)
    {
        builder.ToTable("CheckoutIdempotency");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("Id");

        builder.Property(entity => entity.UserId)
            .HasColumnName("UserId");

        builder.Property(entity => entity.IdempotencyKey)
            .HasColumnName("IdempotencyKey")
            .HasMaxLength(CheckoutIdempotency.IdempotencyKeyMaxLength);

        builder.Property(entity => entity.OrderId)
            .HasColumnName("OrderId");

        builder.Property(entity => entity.RequestFingerprint)
            .HasColumnName("RequestFingerprint")
            .HasMaxLength(CheckoutIdempotency.RequestFingerprintMaxLength);

        builder.Property(entity => entity.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.HasIndex(entity => new { entity.UserId, entity.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_CheckoutIdempotency_UserId_IdempotencyKey");

        builder.HasIndex(entity => entity.OrderId)
            .HasDatabaseName("IX_CheckoutIdempotency_OrderId");
    }
}
