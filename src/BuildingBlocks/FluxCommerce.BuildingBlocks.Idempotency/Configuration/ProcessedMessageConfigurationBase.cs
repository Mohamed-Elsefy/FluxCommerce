using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.BuildingBlocks.Idempotency.Configuration;

/// <summary>
/// Shared EF Core mapping for the canonical <see cref="ProcessedMessage"/> idempotency record.
/// Module DbContexts (Inventory, Orders, Payments) keep isolated <c>ProcessedMessages</c> tables per
/// DbContext to preserve the idempotent consumer boundary (Business State + ProcessedMessage atomic
/// per module), but they share this column mapping so the composite <c>(Key, Handler)</c> primary key
/// and <c>ProcessedAtUtc</c> stay consistent. Inheriting configurations call <c>base.Configure(builder)</c>.
/// </summary>
public abstract class ProcessedMessageConfigurationBase : IEntityTypeConfiguration<ProcessedMessage>
{
    public const int HandlerMaxLength = 128;

    public virtual void Configure(EntityTypeBuilder<ProcessedMessage> builder)
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
