using FluxCommerce.Modules.Identity.Contracts.Enums;

namespace FluxCommerce.Modules.Identity.Contracts.Responses;

/// <summary>
/// Administrative user representation: the public <see cref="UserResponse"/> profile
/// plus lockout visibility. Returned by <c>GET /admin/users</c> and
/// <c>GET /admin/users/{id}</c>.
/// </summary>
public sealed record UserAdminResponse(
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
    int FailedAccessAttemptCount,
    DateTime? LockoutEndUtc,
    bool IsLocked);

/// <summary>
/// Aggregate user counts for the administrative overview.
/// Returned by <c>GET /admin/users/counts</c>.
/// </summary>
public sealed record UserAdminCountsResponse(
    int Total,
    int Pending,
    int Active,
    int Suspended,
    int Locked,
    int Deleted,
    int Customers,
    int Admins,
    int SuperAdmins,
    int Unverified,
    int LockedOut);
