using FluxCommerce.BuildingBlocks.Outbox.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("Id");

        builder.Property(message => message.EventId)
            .HasColumnName("EventId");

        builder.Property(message => message.Type)
            .HasColumnName("Type")
            .HasMaxLength(512);

        builder.Property(message => message.Payload)
            .HasColumnName("Payload");

        builder.Property(message => message.CorrelationId)
            .HasColumnName("CorrelationId");

        builder.Property(message => message.CausationId)
            .HasColumnName("CausationId");

        builder.Property(message => message.OccurredAtUtc)
            .HasColumnName("OccurredAtUtc")
            .HasColumnType("datetime2");

        builder.Property(message => message.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc")
            .HasColumnType("datetime2");

        builder.Property(message => message.ProcessedAtUtc)
            .HasColumnName("ProcessedAtUtc")
            .HasColumnType("datetime2");

        builder.Property(message => message.Status)
            .HasColumnName("Status")
            .HasConversion<int>();

        builder.Property(message => message.RetryCount)
            .HasColumnName("RetryCount");

        builder.Property(message => message.RetryAfterUtc)
            .HasColumnName("RetryAfterUtc")
            .HasColumnType("datetime2");

        builder.Property(message => message.Error)
            .HasColumnName("Error");

        builder.HasIndex(message => new { message.Status, message.CreatedAtUtc })
            .HasDatabaseName("IX_OutboxMessages_Status_CreatedAtUtc");

        builder.HasIndex(message => new { message.Status, message.ProcessedAtUtc })
            .HasDatabaseName("IX_OutboxMessages_Status_ProcessedAtUtc");
    }
}
