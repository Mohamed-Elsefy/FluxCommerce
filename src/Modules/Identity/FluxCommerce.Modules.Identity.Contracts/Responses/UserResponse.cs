using FluxCommerce.Modules.Identity.Contracts.Enums;

namespace FluxCommerce.Modules.Identity.Contracts.Responses;

public sealed record UserResponse(
    Guid Id,
    string Email,
    IReadOnlyCollection<string> Roles,
    bool IsEmailVerified,
    DateTime RegisteredAtUtc,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? PhoneNumber,
    bool IsPhoneVerified,
    string? AvatarUrl,
    string PreferredLanguage,
    string TimeZone,
    UserStatus Status,
    IReadOnlyCollection<string> Permissions);
