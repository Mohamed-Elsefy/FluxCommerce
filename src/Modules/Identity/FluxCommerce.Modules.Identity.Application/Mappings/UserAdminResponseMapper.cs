using FluxCommerce.Modules.Identity.Application.ReadModels;
using FluxCommerce.Modules.Identity.Contracts.Enums;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Mappings;

public static class UserAdminResponseMapper
{
    public static UserAdminResponse ToResponse(UserAdminReadModel user, DateTime utcNow) =>
        new(
            user.Id,
            user.Email,
            user.Roles,
            user.IsEmailVerified,
            user.RegisteredAtUtc,
            user.FirstName,
            user.LastName,
            $"{user.FirstName} {user.LastName}",
            user.PhoneNumber,
            user.IsPhoneVerified,
            user.AvatarUrl,
            user.PreferredLanguage,
            user.TimeZone,
            ToStatus(user.Status),
            user.FailedAccessAttemptCount,
            user.LockoutEndUtc,
            user.LockoutEndUtc is { } lockoutEnd && utcNow < lockoutEnd,
            user.Permissions);

    public static UserAdminCountsResponse ToResponse(UserAdminCountsReadModel counts) =>
        new(
            counts.Total,
            counts.Pending,
            counts.Active,
            counts.Suspended,
            counts.Locked,
            counts.Deleted,
            counts.Customers,
            counts.Admins,
            counts.SuperAdmins,
            counts.Unverified,
            counts.LockedOut);

    private static UserStatus ToStatus(Domain.Enums.UserStatus status) =>
        status switch
        {
            Domain.Enums.UserStatus.Pending => UserStatus.Pending,
            Domain.Enums.UserStatus.Active => UserStatus.Active,
            Domain.Enums.UserStatus.Suspended => UserStatus.Suspended,
            Domain.Enums.UserStatus.Locked => UserStatus.Locked,
            Domain.Enums.UserStatus.Deleted => UserStatus.Deleted,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
