namespace FluxCommerce.Modules.Payments.Application.Interfaces;

public sealed record StripeWebhookVerificationResult(
    bool IsValid,
    string? EventId,
    string? ErrorMessage);

public interface IStripeWebhookService
{
    /// <summary>
    /// Verifies the Stripe webhook signature and parses the event.
    /// </summary>
    /// <param name="payload">The raw request body as string (UTF-8 encoded).</param>
    /// <param name="signatureHeader">The Stripe-Signature header value.</param>
    /// <returns>The verification result containing validity, event ID, and optional error message.</returns>
    StripeWebhookVerificationResult VerifyAndParse(string payload, string signatureHeader);
}