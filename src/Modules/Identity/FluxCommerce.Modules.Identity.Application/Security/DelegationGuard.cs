using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Security;

/// <summary>
/// Anti-escalation guards for delegated user management (ADR-017 D3). A `SuperAdmin` caller is
/// exempt from every check; anyone else is confined below: no assigning the `SuperAdmin` role,
/// no touching `SuperAdmin`-held accounts, and no granting permissions outside their own set.
/// </summary>
public static class DelegationGuard
{
    public static bool IsSuperAdmin(ICurrentUser caller) =>
        caller.Roles.Contains(Roles.SuperAdmin, StringComparer.Ordinal);

    public static Error? CheckRoleAssignable(ICurrentUser caller, string role)
    {
        if (IsSuperAdmin(caller))
            return null;

        return string.Equals(role.Trim(), Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase)
            ? Error.Forbidden("Identity.Forbidden", "The current user is not allowed to assign this role.")
            : null;
    }

    public static Error? CheckTargetAccessible(ICurrentUser caller, User target)
    {
        if (IsSuperAdmin(caller))
            return null;

        return target.Roles.Contains(Roles.SuperAdmin, StringComparer.Ordinal)
            ? Error.Forbidden("Identity.Forbidden", "The current user is not allowed to manage this account.")
            : null;
    }

    public static Error? CheckGrantAllowed(ICurrentUser caller, string permission)
    {
        if (IsSuperAdmin(caller))
            return null;

        return caller.Permissions.Contains(permission.Trim(), StringComparer.Ordinal)
            ? null
            : Error.Forbidden("Identity.Forbidden", "The current user is not allowed to grant this permission.");
    }
}
