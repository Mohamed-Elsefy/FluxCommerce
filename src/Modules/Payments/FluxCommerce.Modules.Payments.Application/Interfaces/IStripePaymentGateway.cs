using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Modules.Payments.Application.Interfaces;

public sealed record CreatePaymentIntentRequest(
    Guid OrderId,
    decimal Amount,
    string Currency);

public sealed record PaymentIntentResult(
    string StripePaymentIntentId,
    PaymentStatus Status,
    string? ClientSecret);

public interface IStripePaymentGateway
{
    /// <summary>
    /// Creates a Stripe PaymentIntent for the given order.
    /// </summary>
    /// <param name="request">The payment intent creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The payment intent result containing the Stripe ID and status.</returns>
    Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current status of a Stripe PaymentIntent.
    /// </summary>
    /// <param name="paymentIntentId">The Stripe PaymentIntent ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current payment status.</returns>
    Task<PaymentStatus> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken cancellationToken = default);
}
