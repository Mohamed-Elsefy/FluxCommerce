using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Payments.Domain.Events;

public sealed class PaymentCancelledDomainEvent : DomainEvent
{
    public Guid PaymentId { get; }
    public Guid OrderId { get; }

    public PaymentCancelledDomainEvent(Guid paymentId, Guid orderId)
    {
        PaymentId = paymentId;
        OrderId = orderId;
    }
}