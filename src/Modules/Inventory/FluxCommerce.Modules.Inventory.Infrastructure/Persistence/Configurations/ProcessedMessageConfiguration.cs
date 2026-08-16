using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>
/// Phase 7.3: maps the canonical <see cref="ProcessedMessage"/> (the shared BuildingBlocks idempotency
/// record, following the <c>OutboxMessage</c> precedent) into the Inventory module DbContext. The
/// idempotency identity decided in Phase 7.2 is <c>EventId + HandlerName</c>, so the composite
/// <c>(Key, Handler)</c> is the primary key — the SQL unique constraint IS the final concurrency
/// protection. <c>Key</c> holds the EventId string (Guid, 36 chars); <c>Handler</c> holds the stable
/// handler identity and is mapped as required so it can participate in the key even though the
/// canonical record declares it nullable. The record stays lightweight: identity fields +
/// <c>ProcessedAtUtc</c>, no payload, no speculative indexes.
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