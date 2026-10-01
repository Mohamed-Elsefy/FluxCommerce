using Microsoft.AspNetCore.Authorization;

namespace FluxCommerce.Api.Authorization;

/// <summary>
/// Requires the specified role, or the <c>SuperAdmin</c> role as a universal bypass
/// (ADR-016 D1). Evaluated by <see cref="RoleOrSuperAdminHandler"/>.
/// </summary>
public sealed class RoleOrSuperAdminRequirement : IAuthorizationRequirement
{
    public string Role { get; }

    public RoleOrSuperAdminRequirement(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("A role name is required.", nameof(role));

        Role = role;
    }
}
