using FluxCommerce.BuildingBlocks.Domain.Events;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.Events;

public sealed class OrderCreated : DomainEvent
{
    public OrderId OrderId { get; }

    public CustomerId CustomerId { get; }

    public OrderNumber OrderNumber { get; }

    public DateTimeOffset OccurredAt { get; }

    public OrderCreated(OrderId orderId, CustomerId customerId, OrderNumber orderNumber, DateTimeOffset occurredAt)
    {
        OrderId = orderId;
        CustomerId = customerId;
        OrderNumber = orderNumber;
        OccurredAt = occurredAt;
    }
}
