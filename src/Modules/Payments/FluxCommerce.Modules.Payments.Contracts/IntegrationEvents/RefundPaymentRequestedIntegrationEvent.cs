using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Phase 10.8: request to refund a succeeded payment after post-payment downstream failure.
/// Published by Orders saga, consumed by Payments.
/// </summary>
public sealed record RefundPaymentRequestedIntegrationEvent(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    decimal Amount,
    string Currency,
    string Reason) : IntegrationEvent
{
    public const string EventType = "payments.refund_payment_requested";
}
