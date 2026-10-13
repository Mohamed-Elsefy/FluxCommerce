using FluxCommerce.Modules.Identity.Domain.Aggregates;
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
    int LockedOut)
{
    /// <summary>
    /// Builds the overview from raw database parts. The store runs the grouping and
    /// counting queries; the status breakdown and role-bucket rules live here in
    /// Application so the store stays a thin persistence adapter.
    /// </summary>
    public static UserAdminCountsReadModel FromParts(
        IEnumerable<(UserStatus Status, int Count)> statusGroups,
        int unverified,
        int lockedOut,
        IEnumerable<IReadOnlyList<string>> roleSets)
    {
        ArgumentNullException.ThrowIfNull(statusGroups);
        ArgumentNullException.ThrowIfNull(roleSets);

        var pending = 0;
        var active = 0;
        var suspended = 0;
        var locked = 0;
        var deleted = 0;

        foreach (var group in statusGroups)
        {
            switch (group.Status)
            {
                case UserStatus.Pending:
                    pending = group.Count;
                    break;
                case UserStatus.Active:
                    active = group.Count;
                    break;
                case UserStatus.Suspended:
                    suspended = group.Count;
                    break;
                case UserStatus.Locked:
                    locked = group.Count;
                    break;
                case UserStatus.Deleted:
                    deleted = group.Count;
                    break;
            }
        }

        var customers = 0;
        var admins = 0;
        var superAdmins = 0;

        foreach (var roles in roleSets)
        {
            if (roles.Contains(Roles.Customer, StringComparer.Ordinal))
                customers++;
            if (roles.Contains(Roles.Admin, StringComparer.Ordinal))
                admins++;
            if (roles.Contains(Roles.SuperAdmin, StringComparer.Ordinal))
                superAdmins++;
        }

        return new UserAdminCountsReadModel(
            pending + active + suspended + locked + deleted,
            pending,
            active,
            suspended,
            locked,
            deleted,
            customers,
            admins,
            superAdmins,
            unverified,
            lockedOut);
    }
}
