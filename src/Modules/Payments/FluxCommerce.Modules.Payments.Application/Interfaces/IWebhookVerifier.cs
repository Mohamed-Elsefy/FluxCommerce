using FluxCommerce.Modules.Payments.Application.Models;

namespace FluxCommerce.Modules.Payments.Application.Interfaces;

/// <summary>
/// Provider webhook verification port: verifies the webhook signature and parses
/// the event. The implementation lives in Infrastructure (currently a Stripe
/// adapter); Application code depends only on this provider-neutral contract.
/// Extracts strongly-typed PaymentIntent reference inside Infrastructure.
/// </summary>
public interface IWebhookVerifier
{
    StripeWebhookVerificationResult VerifyAndParse(string payload, string signatureHeader);
}
