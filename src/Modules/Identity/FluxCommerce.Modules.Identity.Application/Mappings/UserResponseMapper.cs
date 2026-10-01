using FluxCommerce.Modules.Identity.Contracts.Enums;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Mappings;

public static class UserResponseMapper
{
    public static UserResponse ToResponse(User user) =>
        new(
            user.Id,
            user.Email.Value,
            user.Roles,
            user.IsEmailVerified,
            user.RegisteredAtUtc,
            user.FirstName,
            user.LastName,
            user.DisplayName,
            user.PhoneNumber,
            user.IsPhoneVerified,
            user.AvatarUrl,
            user.PreferredLanguage,
            user.TimeZone,
            ToStatus(user.Status),
            user.Permissions);

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
