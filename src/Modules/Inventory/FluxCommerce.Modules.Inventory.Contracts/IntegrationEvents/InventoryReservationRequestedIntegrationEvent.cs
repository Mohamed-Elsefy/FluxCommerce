using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>
/// Integration event issued by the Orders saga orchestrator when it must secure inventory for an
/// order (Phase 8.3). It is the transport command carrying the reservation request to the Inventory
/// module: one event per line item, carrying the canonical <see cref="IntegrationEvent"/> metadata
/// (EventId, CorrelationId, CausationId, Version, OccurredAtUtc) so the workflow stays correlated and
/// causal across the async hop. <see cref="ExpiresAt"/> is the reservation's deterministic deadline
/// (place-time + configured lifetime) used by Inventory to expire reservations. <see cref="EventType"/>
/// is the stable type identifier ("inventory.reservation_requested").
/// </summary>
public sealed record InventoryReservationRequestedIntegrationEvent(
    Guid OrderId,
    string Sku,
    int Quantity,
    DateTimeOffset ExpiresAt)
    : IntegrationEvent
{
    public const string EventType = "inventory.reservation_requested";
}