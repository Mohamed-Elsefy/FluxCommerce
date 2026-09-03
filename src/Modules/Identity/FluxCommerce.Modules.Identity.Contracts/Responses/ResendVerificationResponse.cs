namespace FluxCommerce.Modules.Identity.Contracts.Responses;

public sealed record ResendVerificationResponse(bool EmailSent, DateTime? ExpiresAtUtc);
