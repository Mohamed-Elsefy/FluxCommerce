using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.Modules.Inventory.Contracts.Enums;

namespace FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published by the Inventory module when a reservation is released (Phase 8.7). It
/// is the outcome of an <see cref="InventoryReleaseRequestedIntegrationEvent"/>: the released reservation
/// is terminal (<see cref="Status"/> is <see cref="InventoryReservationStatus.Released"/>) and no stock
/// is held for it anymore. The payload mirrors <see cref="InventoryReservedIntegrationEvent"/> so the
/// snapshot is the same shape in both directions of the workflow. The event carries the canonical
/// <see cref="IntegrationEvent"/> metadata (EventId, CorrelationId, CausationId, Version, OccurredAtUtc):
/// the CorrelationId preserves the workflow correlation across the async hop and the CausationId is the
/// consuming <c>inventory.release_requested</c> event id (Phase 7.8). The Orders module is not
/// required to consume this event (release is fire-and-forget); it is published for observability and
/// for future compensation tracking. <see cref="EventType"/> is the stable type identifier
/// ("inventory.released").
/// </summary>
public sealed record InventoryReleasedIntegrationEvent(
    Guid ReservationId,
    Guid OrderId,
    string Sku,
    int Quantity,
    DateTimeOffset ExpiresAt,
    InventoryReservationStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long ReservationVersion)
    : IntegrationEvent
{
    public const string EventType = "inventory.released";
}