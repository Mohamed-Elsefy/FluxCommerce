using FluxCommerce.Modules.Identity.Application.Interfaces.Security;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeJwtProvider : IJwtProvider
{
    public Guid? LastUserId { get; private set; }

    public string? LastEmail { get; private set; }

    public IReadOnlyCollection<string>? LastRoles { get; private set; }

    public IReadOnlyCollection<string>? LastPermissions { get; private set; }

    public string Token { get; set; } = "access-token";

    public string CreateAccessToken(
        Guid userId,
        string email,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string>? permissions = null)
    {
        LastUserId = userId;
        LastEmail = email;
        LastRoles = roles;
        LastPermissions = permissions;

        return Token;
    }
}
