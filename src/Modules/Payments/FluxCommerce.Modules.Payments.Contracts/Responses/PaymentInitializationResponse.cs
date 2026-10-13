namespace FluxCommerce.Modules.Payments.Contracts.Responses;

/// <summary>
/// Owner-scoped payment initialization data for Stripe.js confirmation.
/// Returned only to the authenticated owner of the order; <c>ClientSecret</c>
/// is runtime-only and must never be stored by the caller.
/// </summary>
public sealed record PaymentInitializationResponse(
    Guid PaymentId,
    Guid OrderId,
    string ClientSecret,
    string? PaymentReference,
    string Status);
