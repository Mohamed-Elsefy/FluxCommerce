using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;

namespace FluxCommerce.BuildingBlocks.Messaging.Consuming;

/// <summary>
/// The transport-agnostic processing context delivered to an integration event handler alongside the
/// deserialized contract. It carries exactly the canonical metadata that survives transport
/// (<see cref="IIntegrationEvent"/>): the event identity (<see cref="EventId"/>), the stable wire
/// type identifier (<see cref="EventType"/>), the contract version (<see cref="EventVersion"/>), the
/// trace identifiers (<see cref="CorrelationId"/>, <see cref="CausationId"/>), and the timestamp
/// (<see cref="OccurredAtUtc"/>). Handlers must never depend on transport or persistence types;
/// the envelope is the only processing context they receive.
/// </summary>
public sealed record IntegrationEventEnvelope
{
    public required Guid EventId { get; init; }

    public required string EventType { get; init; }

    public required EventVersion EventVersion { get; init; }

    public Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }

    public required DateTime OccurredAtUtc { get; init; }

    /// <summary>
    /// The CorrelationId a consumer-produced integration event must carry (Phase 7.8): the incoming
    /// correlation id is preserved across the async hop so the whole workflow stays correlated.
    /// </summary>
    public Guid ProducedEventCorrelationId => CorrelationId;

    /// <summary>
    /// The CausationId a consumer-produced integration event must carry (Phase 7.8): the consumed
    /// event's <see cref="EventId"/> becomes the produced event's causation id, forming the causal
    /// chain (this event caused the produced one). No new correlation/causation model is invented —
    /// the canonical <see cref="IIntegrationEvent"/> metadata is reused.
    /// </summary>
    public Guid ProducedEventCausationId => EventId;

    /// <summary>
    /// Builds the processing envelope from the canonical metadata of a deserialized integration event.
    /// </summary>
    /// <param name="event">The deserialized integration event.</param>
    /// <param name="eventType">The stable wire type identifier of the event (e.g. <c>orders.order_created</c>).</param>
    public static IntegrationEventEnvelope From(IIntegrationEvent @event, string eventType) => new()
    {
        EventId = @event.EventId,
        EventType = eventType,
        EventVersion = @event.Version,
        CorrelationId = @event.CorrelationId,
        CausationId = @event.CausationId,
        OccurredAtUtc = @event.OccurredAtUtc
    };
}
