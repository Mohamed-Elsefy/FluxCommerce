using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Payments.Domain.Events;

public sealed class PaymentRefundedDomainEvent : DomainEvent
{
    public Guid PaymentId { get; }
    public Guid OrderId { get; }
    public decimal Amount { get; }
    public string? Currency { get; }
    public string? ProviderPaymentIntentId { get; }
    public string? ProviderRefundId { get; }

    public PaymentRefundedDomainEvent(Guid paymentId, Guid orderId, decimal amount, string? currency, string? providerPaymentIntentId = null, string? providerRefundId = null)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        ProviderPaymentIntentId = providerPaymentIntentId;
        ProviderRefundId = providerRefundId;
    }
}
