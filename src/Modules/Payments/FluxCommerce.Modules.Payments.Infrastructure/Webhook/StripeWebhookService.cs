using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Domain.Options;
using Microsoft.Extensions.Options;
using Stripe;
using System.Text.Json;

namespace FluxCommerce.Modules.Payments.Infrastructure.Webhook;

public sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly string _webhookSecret;

    public StripeWebhookService(IOptions<StripeOptions> stripeOptions)
    {
        _webhookSecret = stripeOptions.Value.WebhookSecret ?? throw new InvalidOperationException("Stripe Webhook Secret is not configured.");
    }

    public StripeWebhookVerificationResult VerifyAndParse(string payload, string signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return new StripeWebhookVerificationResult(false, null, null, null, "Stripe-Signature header is missing");
        }

        if (string.IsNullOrEmpty(payload))
        {
            return new StripeWebhookVerificationResult(false, null, null, null, "Payload is empty");
        }

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _webhookSecret, tolerance: 300, throwOnApiVersionMismatch: false);

            var paymentIntentId = ExtractPaymentIntentId(payload);

            return new StripeWebhookVerificationResult(true, stripeEvent.Id, stripeEvent.Type, paymentIntentId, null);
        }
        catch (StripeException)
        {
            return new StripeWebhookVerificationResult(false, null, null, null, "Invalid Stripe webhook signature");
        }
    }

    private static string? ExtractPaymentIntentId(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj) &&
                obj.TryGetProperty("id", out var idProp))
            {
                var id = idProp.GetString();
                if (!string.IsNullOrEmpty(id) && id.StartsWith("pi_", StringComparison.Ordinal))
                    return id;
            }
        }
        catch { }
        return null;
    }
}

