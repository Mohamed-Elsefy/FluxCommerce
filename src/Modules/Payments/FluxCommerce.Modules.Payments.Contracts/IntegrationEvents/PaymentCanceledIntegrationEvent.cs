using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when a Stripe webhook transitions a Payment to Cancelled
/// (<c>payment_intent.canceled</c>). Lets the Orders saga leave <c>WaitingForPayment</c> for
/// compensation instead of hanging until reservation expiry. Lives in Payments.Contracts
/// (not BuildingBlocks), consistent with ADR-007.
/// </summary>
public sealed record PaymentCanceledIntegrationEvent(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    string Reason,
    string PaymentStatus,
    DateTimeOffset ProcessedAtUtc) : IntegrationEvent
{
    public const string EventType = "payments.payment_canceled";
}
