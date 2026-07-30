namespace FluxCommerce.BuildingBlocks.Messaging.Metadata;

public sealed record EventMetadata
{
    public Guid EventId { get; init; }
    public Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string Version { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public string EventType { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    public EventMetadata(
        Guid correlationId,
        string eventType,
        Guid? causationId = null,
        string version = "1.0")
    {
        EventId = Guid.NewGuid();
        CorrelationId = correlationId;
        CausationId = causationId;
        Version = version;
        OccurredAtUtc = DateTime.UtcNow;
        EventType = eventType;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
