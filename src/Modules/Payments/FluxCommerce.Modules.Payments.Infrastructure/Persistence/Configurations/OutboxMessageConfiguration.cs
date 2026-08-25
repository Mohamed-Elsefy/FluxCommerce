using FluxCommerce.BuildingBlocks.Outbox.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Payments.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("Id");
        builder.Property(m => m.EventId).HasColumnName("EventId");
        builder.Property(m => m.Type).HasColumnName("Type").HasMaxLength(512);
        builder.Property(m => m.Payload).HasColumnName("Payload");
        builder.Property(m => m.CorrelationId).HasColumnName("CorrelationId");
        builder.Property(m => m.CausationId).HasColumnName("CausationId");
        builder.Property(m => m.OccurredAtUtc).HasColumnName("OccurredAtUtc").HasColumnType("datetime2");
        builder.Property(m => m.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");
        builder.Property(m => m.ProcessedAtUtc).HasColumnName("ProcessedAtUtc").HasColumnType("datetime2");
        builder.Property(m => m.Status).HasColumnName("Status").HasConversion<int>();
        builder.Property(m => m.RetryCount).HasColumnName("RetryCount");
        builder.Property(m => m.RetryAfterUtc).HasColumnName("RetryAfterUtc").HasColumnType("datetime2");
        builder.Property(m => m.Error).HasColumnName("Error");
        builder.Property(m => m.TraceParent).HasColumnName("TraceParent").HasMaxLength(55);
        builder.Property(m => m.TraceState).HasColumnName("TraceState").HasMaxLength(512);

        builder.HasIndex(m => new { m.Status, m.CreatedAtUtc }).HasDatabaseName("IX_OutboxMessages_Status_CreatedAtUtc");
        builder.HasIndex(m => new { m.Status, m.ProcessedAtUtc }).HasDatabaseName("IX_OutboxMessages_Status_ProcessedAtUtc");
    }
}
