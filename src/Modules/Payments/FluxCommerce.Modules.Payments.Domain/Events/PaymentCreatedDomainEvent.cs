using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Payments.Domain.Events;

public sealed class PaymentCreatedDomainEvent : DomainEvent
{
    public Guid PaymentId { get; }
    public Guid OrderId { get; }
    public decimal Amount { get; }
    public string Currency { get; }

    public PaymentCreatedDomainEvent(Guid paymentId, Guid orderId, decimal amount, string currency)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
    }
}