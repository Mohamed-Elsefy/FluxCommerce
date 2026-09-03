using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when an inventory reservation expires (Phase 11.2). It is the outcome
/// of the Inventory reservation reaching its deterministic deadline: the reservation is terminal
/// (expired) and no stock is held for it anymore. It is the cross-module signal that tells the Orders
/// saga the secured inventory is no longer available, so the saga can apply its expiry rules
/// (Phase 11.5/11.6). The payload is flat and free of Inventory internals: only the reservation
/// identity, the owning order, the reserved line (<see cref="Sku"/>/<see cref="Quantity"/>) and the
/// exact expiry instant (<see cref="ExpiredAtUtc"/>). The event carries the canonical
/// <see cref="IntegrationEvent"/> metadata (EventId, CorrelationId, CausationId, Version,
/// OccurredAtUtc): the CorrelationId preserves the workflow correlation across the async hop and the
/// CausationId is the producing domain-event id. It resides in the Inventory module Contracts project
/// because Inventory owns the reservation lifecycle and therefore owns this event (Phase 11.2).
/// <see cref="EventType"/> is the stable type identifier ("inventory.reservation_expired").
/// </summary>
public sealed record InventoryReservationExpiredIntegrationEvent(
    Guid ReservationId,
    Guid OrderId,
    string Sku,
    int Quantity,
    DateTimeOffset ExpiredAtUtc)
    : IntegrationEvent
{
    public const string EventType = "inventory.reservation_expired";
}