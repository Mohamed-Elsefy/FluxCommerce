using FluxCommerce.Modules.Identity.Domain.Enums;

namespace FluxCommerce.Modules.Identity.Application.ReadModels;

/// <summary>
/// Read-side projection of a <c>User</c> for the administrative user management queries.
/// </summary>
public sealed record UserAdminReadModel(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsEmailVerified,
    DateTime RegisteredAtUtc,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool IsPhoneVerified,
    string? AvatarUrl,
    string PreferredLanguage,
    string TimeZone,
    UserStatus Status,
    int FailedAccessAttemptCount,
    DateTime? LockoutEndUtc,
    IReadOnlyList<string> Permissions);

/// <summary>
/// Aggregate user counts for the administrative overview.
/// </summary>
public sealed record UserAdminCountsReadModel(
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
