using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Payments.Domain.Events;

public sealed class PaymentFailedDomainEvent : DomainEvent
{
    public Guid PaymentId { get; }
    public Guid OrderId { get; }
    public decimal Amount { get; }
    public string? Currency { get; }
    public string FailureReason { get; }

    public PaymentFailedDomainEvent(Guid paymentId, Guid orderId, decimal amount, string? currency, string failureReason)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        FailureReason = failureReason;
    }
}