using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>
/// Integration event issued by the Orders saga orchestrator when a compensation release of previously
/// reserved inventory is required (Phase 8.7). It is the transport command carrying the release request
/// to the Inventory module: the order was cancelled after its reservations were applied, so each
/// recorded reservation must be released. It carries only the <see cref="ReservationId"/> — Inventory
/// owns the reservation lifecycle and releases by reservation identity (ADR-009). The event carries the
/// canonical <see cref="IntegrationEvent"/> metadata (EventId, CorrelationId, CausationId, Version,
/// OccurredAtUtc): the CorrelationId preserves the workflow correlation (the incoming
/// <c>orders.order_cancelled</c> correlation) and the CausationId is the consuming event id. Replaying
/// the same event deterministically produces the same release request, which makes the release safe
/// under at-least-once delivery (the Inventory release consumer is idempotent, Phase 8.7). As a command
/// the event is fire-and-forget: the Inventory module never acknowledges the release back to the saga.
/// <see cref="EventType"/> is the stable type identifier ("inventory.release_requested").
/// </summary>
public sealed record InventoryReleaseRequestedIntegrationEvent(
    Guid ReservationId)
    : IntegrationEvent
{
    public const string EventType = "inventory.release_requested";
}