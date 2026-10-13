using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Integration event published when Payments creates (or reuses) the Stripe PaymentIntent
/// for an order. Lets the Orders saga record the payment correlation
/// (<c>MarkPaymentRequested</c>) so later steps (e.g. refund) use tracked ids instead of
/// placeholders. Lives in Payments.Contracts (not BuildingBlocks), consistent with ADR-007.
/// </summary>
public sealed record PaymentInitiatedIntegrationEvent(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    DateTimeOffset ProcessedAtUtc) : IntegrationEvent
{
    public const string EventType = "payments.payment_initiated";
}
