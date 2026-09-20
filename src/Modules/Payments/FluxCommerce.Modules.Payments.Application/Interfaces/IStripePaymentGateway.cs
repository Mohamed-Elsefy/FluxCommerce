using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Modules.Payments.Application.Interfaces;

public interface IStripePaymentGateway
{
    /// <summary>
    /// Creates a Stripe PaymentIntent for the given order using a deterministic idempotency key.
    /// Idempotency key format: intent_{OrderId}_{PaymentId}_{Version}
    /// </summary>
    Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current status of a Stripe PaymentIntent.
    /// </summary>
    Task<PaymentStatus> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-fetches the live client secret of an existing Stripe PaymentIntent.
    /// Used by the owner-scoped initialization endpoint so retries reuse the single
    /// intent per order instead of minting new ones. Returns null when Stripe
    /// no longer exposes a secret for the intent.
    /// </summary>
    Task<string?> RetrieveClientSecretAsync(string paymentIntentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 10.8: creates a Stripe Refund for a succeeded PaymentIntent using a deterministic idempotency key.
    /// Idempotency key format: refund_{PaymentId}_{OrderId}
    /// </summary>
    Task<RefundResult> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken = default);
}
