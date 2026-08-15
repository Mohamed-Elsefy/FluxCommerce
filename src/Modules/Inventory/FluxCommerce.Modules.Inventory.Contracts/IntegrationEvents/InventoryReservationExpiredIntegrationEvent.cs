using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when an inventory reservation expires. Flat, primitive-only business
/// payload with the canonical <see cref="IntegrationEvent"/> metadata. <see cref="EventType"/> is the
/// stable type identifier ("inventory.reservation_expired").
/// </summary>
public sealed record InventoryReservationExpiredIntegrationEvent(
    Guid ReservationId,
    Guid OrderId,
    string Sku,
    int Quantity,
    DateTimeOffset OccurredAt)
    : IntegrationEvent
{
    public const string EventType = "inventory.reservation_expired";
}