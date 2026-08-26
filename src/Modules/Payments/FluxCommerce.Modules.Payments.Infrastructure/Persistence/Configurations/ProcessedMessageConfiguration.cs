using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Phase 10.3: maps the canonical <see cref="ProcessedMessage"/> into the Payments DbContext
/// for idempotent consumption of <c>payments.create_payment_requested</c>. The composite
/// <c>(Key, Handler)</c> (EventId + HandlerName) is the primary key — the SQL constraint
/// is the final concurrency arbiter for duplicate deliveries. Mirrors
/// <c>Orders/Inventory ProcessedMessageConfiguration</c>.
/// </summary>
public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public const int HandlerMaxLength = 128;

    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("ProcessedMessages");

        builder.HasKey(message => new { message.Key, message.Handler });

        builder.Property(message => message.Key)
            .HasColumnName("Key")
            .HasMaxLength(36);

        builder.Property(message => message.Handler)
            .HasColumnName("Handler")
            .HasMaxLength(HandlerMaxLength)
            .IsRequired();

        builder.Property(message => message.ProcessedAtUtc)
            .HasColumnName("ProcessedAtUtc")
            .HasColumnType("datetime2");
    }
}
