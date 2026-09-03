using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published by the Inventory module when a reservation request fails for a business
/// reason (Phase 8.5). It is the failure result of an <see cref="InventoryReservationRequestedIntegrationEvent"/>:
/// the reservation was NOT created and no stock is held. <see cref="Reason"/> carries the stable
/// Inventory error code (e.g. <c>Inventory.InsufficientStock</c>) so the Orders saga can apply the
/// appropriate failure handling (Phase 8.6). The payload is flat and free of Inventory internals, and
/// the event carries the canonical <see cref="IntegrationEvent"/> metadata (EventId, CorrelationId,
/// CausationId, Version, OccurredAtUtc): the CorrelationId preserves the workflow correlation across
/// the async hop and the CausationId is the consuming <c>inventory.reservation_requested</c> event id
/// (Phase 7.8). <see cref="EventType"/> is the stable type identifier ("inventory.reservation_failed").
/// </summary>
public sealed record InventoryReservationFailedIntegrationEvent(
    Guid OrderId,
    string Sku,
    int Quantity,
    string Reason)
    : IntegrationEvent
{
    public const string EventType = "inventory.reservation_failed";
}