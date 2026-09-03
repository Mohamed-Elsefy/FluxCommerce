using FluxCommerce.BuildingBlocks.Domain.Events;

namespace FluxCommerce.Modules.Payments.Domain.Events;

public sealed class PaymentSucceededDomainEvent : DomainEvent
{
    public Guid PaymentId { get; }
    public Guid OrderId { get; }
    public decimal Amount { get; }
    public string? Currency { get; }
    public string? ProviderPaymentIntentId { get; }
    public string? Provider { get; }

    public PaymentSucceededDomainEvent(Guid paymentId, Guid orderId, decimal amount, string? currency, string? providerPaymentIntentId = null, string? provider = null)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        ProviderPaymentIntentId = providerPaymentIntentId;
        Provider = provider;
    }
}