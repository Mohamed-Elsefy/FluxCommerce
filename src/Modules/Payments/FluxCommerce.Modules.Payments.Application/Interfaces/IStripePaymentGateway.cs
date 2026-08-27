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
    /// Phase 10.8: creates a Stripe Refund for a succeeded PaymentIntent using a deterministic idempotency key.
    /// Idempotency key format: refund_{PaymentId}_{OrderId}
    /// </summary>
    Task<RefundResult> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreatePaymentIntentRequest(
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    long Version);

public sealed record PaymentIntentResult(
    string StripePaymentIntentId,
    PaymentStatus Status,
    string? ClientSecret);

public sealed record CreateRefundRequest(
    Guid OrderId,
    Guid PaymentId,
    string ProviderPaymentIntentId,
    decimal Amount,
    string Currency);

public sealed record RefundResult(
    string StripeRefundId,
    string Status);
