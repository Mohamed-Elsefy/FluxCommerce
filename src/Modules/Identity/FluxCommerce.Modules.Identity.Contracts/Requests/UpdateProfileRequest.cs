namespace FluxCommerce.Modules.Identity.Contracts.Requests;

/// <summary>
/// Partial profile update for the authenticated user. Every field is optional:
/// <c>null</c> leaves the current value unchanged, while an empty value clears an
/// optional field (phone number, avatar URL).
/// </summary>
public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? AvatarUrl,
    string? PreferredLanguage,
    string? TimeZone);
