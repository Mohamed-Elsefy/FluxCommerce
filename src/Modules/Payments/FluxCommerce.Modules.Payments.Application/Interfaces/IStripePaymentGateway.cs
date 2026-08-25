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
