using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Infrastructure.Observability;
using Stripe;
using System.Diagnostics;

namespace FluxCommerce.Modules.Payments.Infrastructure.Gateways;

public sealed class StripePaymentGateway : IStripePaymentGateway
{
    public StripePaymentGateway()
    {
    }

    /// <summary>
    /// Generates a deterministic Stripe Idempotency Key from the business operation context.
    /// Format: intent_{OrderId}_{PaymentId}_{Version}
    /// </summary>
    public static string GenerateIdempotencyKey(Guid orderId, Guid paymentId, long version)
    {
        return $"intent_{orderId}_{paymentId}_{version}";
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentsActivitySource.Source.StartActivity("Payments.Stripe.CreatePaymentIntent", ActivityKind.Internal);
        activity?.SetTag("order.id", request?.OrderId);
        activity?.SetTag("payment.id", request?.PaymentId);
        activity?.SetTag("payment.amount", request?.Amount);
        activity?.SetTag("payment.currency", request?.Currency);
        activity?.SetTag("payment.version", request?.Version);

        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.OrderId == Guid.Empty) throw new ArgumentException("OrderId must not be empty.", nameof(request.OrderId));
        if (request.PaymentId == Guid.Empty) throw new ArgumentException("PaymentId must not be empty.", nameof(request.PaymentId));
        if (string.IsNullOrWhiteSpace(request.Currency)) throw new ArgumentException("Currency is required.", nameof(request.Currency));
        if (request.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(request.Amount));

        var service = new PaymentIntentService();
        var createOptions = new PaymentIntentCreateOptions
        {
            Amount = (long)(request.Amount * 100m),
            Currency = request.Currency.ToLowerInvariant(),
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = new Dictionary<string, string>
            {
                ["order_id"] = request.OrderId.ToString(),
                ["payment_id"] = request.PaymentId.ToString()
            }
        };

        var idempotencyKey = GenerateIdempotencyKey(request.OrderId, request.PaymentId, request.Version);
        var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };

        // Stripe .NET SDK does not expose async overload with CancellationToken; use synchronous call wrapped in Task.Run
        var paymentIntent = await Task.Run(() => service.Create(createOptions, requestOptions), cancellationToken);
        activity?.SetTag("stripe.payment_intent_id", paymentIntent.Id);
        activity?.SetTag("stripe.status", paymentIntent.Status);

        return new PaymentIntentResult(
            StripePaymentIntentId: paymentIntent.Id,
            Status: MapStripeStatusToPaymentStatus(paymentIntent.Status),
            ClientSecret: paymentIntent.ClientSecret);
    }

    public async Task<PaymentStatus> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId)) throw new ArgumentException("Value cannot be null or empty.", nameof(paymentIntentId));

        var service = new PaymentIntentService();
        var paymentIntent = await Task.Run(() => service.Get(paymentIntentId), cancellationToken);
        return MapStripeStatusToPaymentStatus(paymentIntent.Status);
    }

    private static PaymentStatus MapStripeStatusToPaymentStatus(string stripeStatus)
    {
        return stripeStatus switch
        {
            "requires_payment_method" => PaymentStatus.Pending,
            "requires_action" => PaymentStatus.Pending,
            "processing" => PaymentStatus.Processing,
            "succeeded" => PaymentStatus.Succeeded,
            "canceled" => PaymentStatus.Cancelled,
            _ => PaymentStatus.Pending
        };
    }
}
