using FluxCommerce.Modules.Payments.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Payments.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(payment => payment.PaymentId);

        builder.Property(payment => payment.PaymentId)
            .HasColumnName("PaymentId");

        builder.Property(payment => payment.OrderId)
            .HasColumnName("OrderId");

        builder.Property(payment => payment.Currency)
            .HasColumnName("Currency")
            .HasMaxLength(3);

        builder.Property(payment => payment.Amount)
            .HasColumnName("Amount")
            .HasPrecision(18, 6);

        builder.Property(payment => payment.ProviderPaymentIntentId)
            .HasColumnName("ProviderPaymentIntentId")
            .HasMaxLength(256);

        builder.Property(payment => payment.Provider)
            .HasColumnName("Provider")
            .HasMaxLength(128);

        builder.Property(payment => payment.Version)
            .HasColumnName("Version")
            .IsConcurrencyToken();

        builder.Property(payment => payment.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(payment => payment.UpdatedAtUtc)
            .HasColumnName("UpdatedAtUtc");

        builder.HasIndex(payment => payment.ProviderPaymentIntentId)
            .IsUnique()
            .HasDatabaseName("UX_Payments_ProviderPaymentIntentId");

        builder.HasIndex(payment => payment.OrderId)
            .IsUnique()
            .HasDatabaseName("UX_Payments_OrderId");

        builder.HasIndex(payment => payment.CreatedAtUtc)
            .HasDatabaseName("IX_Payments_CreatedAtUtc");
    }
}