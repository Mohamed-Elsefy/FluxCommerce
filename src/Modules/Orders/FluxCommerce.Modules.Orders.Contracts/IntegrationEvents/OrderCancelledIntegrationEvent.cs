using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when an order is cancelled. Flat, primitive-only business payload with
/// the canonical <see cref="IntegrationEvent"/> metadata. <see cref="EventType"/> is the stable type
/// identifier ("orders.order_cancelled").
/// </summary>
public sealed record OrderCancelledIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    DateTimeOffset OccurredAt)
    : IntegrationEvent
{
    public const string EventType = "orders.order_cancelled";
}