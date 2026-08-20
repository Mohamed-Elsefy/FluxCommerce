using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.Modules.Inventory.Contracts.Enums;

namespace FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published by the Inventory module when a reservation request succeeds (Phase 8.5).
/// It is the success result of an <see cref="InventoryReservationRequestedIntegrationEvent"/> and
/// carries the created reservation snapshot back to the Orders saga (the Orders result consumer and
/// the Order lifecycle transition that consumes it are Phase 8.6). The payload is flat and stays free
/// of Inventory internals: <see cref="Status"/> uses the contract-level
/// <see cref="InventoryReservationStatus"/> mirror and <see cref="ReservationVersion"/> is the
/// reservation aggregate version, so inter-module consumers never depend on the Inventory domain. It
/// carries the canonical <see cref="IntegrationEvent"/> metadata (EventId, CorrelationId, CausationId,
/// Version, OccurredAtUtc): the CorrelationId preserves the workflow correlation across the async hop
/// and the CausationId is the consuming <c>inventory.reservation_requested</c> event id (Phase 7.8).
/// The payload field is named <c>ReservationVersion</c> because the metadata base already owns
/// <c>Version</c> (the wire contract version). <see cref="EventType"/> is the stable type identifier
/// ("inventory.reserved").
/// </summary>
public sealed record InventoryReservedIntegrationEvent(
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
    public const string EventType = "inventory.reserved";
}