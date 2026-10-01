using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.AspNetCore.Authorization;

namespace FluxCommerce.Api.Authorization;

/// <summary>
/// Requires one action-level permission from the <see cref="PermissionCatalog"/>, or the
/// <c>SuperAdmin</c> role as a universal bypass (ADR-016 D1/D2).
/// Evaluated by <see cref="PermissionOrSuperAdminHandler"/>.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("A permission name is required.", nameof(permission));

        if (!PermissionCatalog.IsDefined(permission.Trim()))
            throw new ArgumentException($"Unknown permission '{permission.Trim()}'.", nameof(permission));

        Permission = permission.Trim();
    }
}
