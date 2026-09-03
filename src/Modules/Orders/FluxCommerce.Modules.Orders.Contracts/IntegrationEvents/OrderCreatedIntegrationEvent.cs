using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when an order is placed. The business payload is flat and primitive-only
/// (no Domain value objects, EF, or Infrastructure types), and the contract derives from
/// <see cref="IntegrationEvent"/> so the canonical metadata (EventId, CorrelationId, CausationId,
/// Version, OccurredAtUtc) travels with it. <see cref="EventType"/> is the stable, versioned-on-routing
/// type identifier ("orders.order_created").
/// </summary>
public sealed record OrderCreatedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    DateTimeOffset OccurredAt)
    : IntegrationEvent
{
    public const string EventType = "orders.order_created";
}