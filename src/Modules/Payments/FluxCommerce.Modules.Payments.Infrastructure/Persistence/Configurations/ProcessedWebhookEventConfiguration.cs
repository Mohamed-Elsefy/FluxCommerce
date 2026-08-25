using FluxCommerce.Modules.Payments.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Payments.Infrastructure.Persistence.Configurations;

public sealed class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> builder)
    {
        builder.ToTable("ProcessedWebhookEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("Id");

        builder.Property(e => e.StripeEventId)
            .HasColumnName("StripeEventId")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnName("EventType")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.ProcessedAtUtc)
            .HasColumnName("ProcessedAtUtc")
            .IsRequired();

        builder.HasIndex(e => e.StripeEventId)
            .IsUnique()
            .HasDatabaseName("UX_ProcessedWebhookEvents_StripeEventId");
    }
}
