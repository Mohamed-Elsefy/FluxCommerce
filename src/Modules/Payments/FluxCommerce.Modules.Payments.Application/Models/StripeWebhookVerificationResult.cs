namespace FluxCommerce.Modules.Payments.Application.Models;

public sealed record StripeWebhookVerificationResult(
    bool IsValid,
    string? EventId,
    string? EventType,
    string? PaymentIntentId,
    string? ErrorMessage);
