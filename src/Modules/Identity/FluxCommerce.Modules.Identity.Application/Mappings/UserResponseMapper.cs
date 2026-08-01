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
            user.RegisteredAtUtc);
}
