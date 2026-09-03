using FluxCommerce.Modules.Payments.Application.Models;

namespace FluxCommerce.Modules.Payments.Application.Interfaces;

public interface IStripeWebhookService
{
    /// <summary>
    /// Verifies the Stripe webhook signature and parses the event.
    /// Extracts strongly-typed PaymentIntent reference inside Infrastructure.
    /// </summary>
    StripeWebhookVerificationResult VerifyAndParse(string payload, string signatureHeader);
}
