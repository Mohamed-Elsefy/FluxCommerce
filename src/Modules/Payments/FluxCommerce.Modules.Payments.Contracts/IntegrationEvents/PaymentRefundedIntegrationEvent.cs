using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Phase 10.8: published after a Stripe refund is successfully created and Payment transitions to Refunded.
/// </summary>
public sealed record PaymentRefundedIntegrationEvent(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    string? ProviderRefundId,
    decimal Amount,
    string Currency,
    DateTimeOffset RefundedAtUtc) : IntegrationEvent
{
    public const string EventType = "payments.payment_refunded";
}
