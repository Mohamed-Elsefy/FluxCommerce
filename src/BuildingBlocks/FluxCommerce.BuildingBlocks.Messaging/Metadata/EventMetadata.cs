using FluxCommerce.BuildingBlocks.Messaging.Versioning;

namespace FluxCommerce.BuildingBlocks.Messaging.Metadata;

public sealed record EventMetadata
{
    public Guid EventId { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }

    public EventVersion Version { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public string EventType { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public EventMetadata(
        Guid correlationId,
        string eventType,
        Guid? causationId = null,
        EventVersion? version = null)
    {
        EventId = Guid.NewGuid();
        CorrelationId = correlationId;
        CausationId = causationId;
        Version = version ?? EventVersion.V1;
        OccurredAtUtc = DateTime.UtcNow;
        EventType = eventType;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
