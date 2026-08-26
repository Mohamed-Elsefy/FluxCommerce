using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Phase 10.3: async contract from the Order Saga to the Payments module to request
/// payment intent creation. Lives in <c>Payments.Contracts</c> (owner = Payments) so
/// Orders orchestrator can publish via the outbox without referencing Payments infrastructure
/// or Stripe. The saga stages this event after <c>inventory.reserved</c> transitions the saga
/// to <c>WaitingForPayment</c>. Payments owns intent creation and idempotency (unique OrderId).
/// Carries authoritative amount/currency/customer from the Order snapshot.
/// </summary>
public sealed record CreatePaymentRequestedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency) : IntegrationEvent
{
    public const string EventType = "payments.create_payment_requested";
}
