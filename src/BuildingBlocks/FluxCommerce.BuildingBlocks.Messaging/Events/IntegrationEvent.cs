namespace FluxCommerce.BuildingBlocks.Messaging.Events;

public abstract class IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public Guid CorrelationId { get; }
    public Guid? CausationId { get; }
    public string Version { get; }
    public DateTime OccurredAtUtc { get; } = DateTime.UtcNow;

    protected IntegrationEvent(Guid correlationId, Guid? causationId = null, string version = "1.0")
    {
        CorrelationId = correlationId;
        CausationId = causationId;
        Version = version;
    }
}
