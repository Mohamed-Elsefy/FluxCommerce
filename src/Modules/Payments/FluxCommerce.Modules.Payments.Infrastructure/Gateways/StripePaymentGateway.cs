using Stripe;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Application.Interfaces;

namespace FluxCommerce.Modules.Payments.Infrastructure.Gateways;

public sealed class StripePaymentGateway : IStripePaymentGateway
{
    public StripePaymentGateway()
    {
        // StripeConfiguration.ApiKey is set externally via IOptions<StripeOptions>
        // by the API host at application startup.
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var service = new PaymentIntentService();
        var createOptions = new PaymentIntentCreateOptions
        {
            Amount = (long)(request.Amount * 100),
            Currency = request.Currency.ToLowerInvariant(),
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = new Dictionary<string, string>
            {
                ["order_id"] = request.OrderId.ToString()
            }
        };

        // Use synchronous call wrapped in Task for interface compatibility;
        // CancellationToken is accepted per interface contract but the Stripe
        // .NET SDK v42.3.0 does not accept it directly on Create()/Get().
        var paymentIntent = await Task.Run(() => service.Create(createOptions), cancellationToken);

        return new PaymentIntentResult(
            StripePaymentIntentId: paymentIntent.Id,
            Status: MapStripeStatusToPaymentStatus(paymentIntent.Status),
            ClientSecret: paymentIntent.ClientSecret);
    }

    public async Task<PaymentStatus> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId)) throw new ArgumentException("Value cannot be null or empty.", nameof(paymentIntentId));

        var service = new PaymentIntentService();
        // Same pattern: synchronous call wrapped in Task.Run for interface compliance.
        var paymentIntent = await Task.Run(() => service.Get(paymentIntentId), cancellationToken);

        return MapStripeStatusToPaymentStatus(paymentIntent.Status);
    }

    private static PaymentStatus MapStripeStatusToPaymentStatus(string stripeStatus)
    {
        return stripeStatus switch
        {
            "requires_payment_method" => PaymentStatus.Pending,
            "processing" => PaymentStatus.Processing,
            "succeeded" => PaymentStatus.Succeeded,
            "failed" => PaymentStatus.Failed,
            "canceled" => PaymentStatus.Cancelled,
            _ => PaymentStatus.Pending
        };
    }
}
