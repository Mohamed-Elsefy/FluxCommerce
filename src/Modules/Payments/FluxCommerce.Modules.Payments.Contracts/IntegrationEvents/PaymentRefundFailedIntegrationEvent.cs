using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Phase 10.8 optional: published when a refund was attempted but failed permanently.
/// Not used for compensation yet, but reserved for observability.
/// </summary>
public sealed record PaymentRefundFailedIntegrationEvent(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    string FailureReason,
    DateTimeOffset FailedAtUtc) : IntegrationEvent
{
    public const string EventType = "payments.payment_refund_failed";
}
