using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;

/// <summary>
/// Phase 10.4 integration event published when an order is confirmed after successful payment.
/// </summary>
public sealed record OrderConfirmedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    DateTimeOffset OccurredAt) : IntegrationEvent
{
    public const string EventType = "orders.order_confirmed";
}
