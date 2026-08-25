using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when a Stripe webhook transitions a Payment to Failed.
/// Lives in Payments.Contracts (not BuildingBlocks).
/// </summary>
public sealed record PaymentFailedIntegrationEvent(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    string FailureReason,
    string PaymentStatus,
    DateTimeOffset ProcessedAtUtc) : IntegrationEvent
{
    public const string EventType = "payments.payment_failed";
}
