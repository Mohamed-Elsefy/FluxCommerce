using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when a Stripe webhook transitions a Payment to Succeeded.
/// Lives in Payments.Contracts (not BuildingBlocks), consistent with ADR-007.
/// </summary>
public sealed record PaymentSucceededIntegrationEvent(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    decimal Amount,
    string Currency,
    string PaymentStatus,
    DateTimeOffset ProcessedAtUtc) : IntegrationEvent
{
    public const string EventType = "payments.payment_succeeded";
}
