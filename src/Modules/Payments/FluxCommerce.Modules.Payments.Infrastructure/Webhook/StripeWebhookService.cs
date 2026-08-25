using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Domain.Options;
using Microsoft.Extensions.Options;
using Stripe;

namespace FluxCommerce.Modules.Payments.Infrastructure.Webhook;

public sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly string _webhookSecret;

    public StripeWebhookService(IOptions<StripeOptions> stripeOptions)
    {
        _webhookSecret = stripeOptions.Value.WebhookSecret ?? throw new InvalidOperationException("Stripe Webhook Secret is not configured.");
    }

    /// <summary>
    /// Verifies the Stripe webhook signature and parses the event.
    /// </summary>
    /// <param name="payload">The raw request body as string (UTF-8 encoded).</param>
    /// <param name="signatureHeader">The Stripe-Signature header value.</param>
    /// <returns>The verification result containing validity, event ID, and optional error message.</returns>
    public StripeWebhookVerificationResult VerifyAndParse(string payload, string signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            // Note: logger not available here; caller should log
            return new StripeWebhookVerificationResult(
                IsValid: false,
                EventId: null,
                ErrorMessage: "Stripe-Signature header is missing");
        }

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _webhookSecret);

            // Note: logger not available here; caller should log
            return new StripeWebhookVerificationResult(
                IsValid: true,
                EventId: stripeEvent?.Id,
                ErrorMessage: null);
        }
        catch (StripeException)
        {
            // Invalid signature or payload mismatch
            return new StripeWebhookVerificationResult(
                IsValid: false,
                EventId: null,
                ErrorMessage: "Invalid Stripe webhook signature");
        }
    }
}