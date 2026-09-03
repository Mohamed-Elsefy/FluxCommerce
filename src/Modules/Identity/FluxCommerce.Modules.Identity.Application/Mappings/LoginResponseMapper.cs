using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Entities;

namespace FluxCommerce.Modules.Identity.Application.Mappings;

public static class LoginResponseMapper
{
    public static LoginResponse ToResponse(User user, string accessToken, RefreshToken refreshToken) =>
        new(
            user.Id,
            user.Email.Value,
            user.Roles,
            accessToken,
            refreshToken.Value,
            refreshToken.ExpiresAtUtc);
}
