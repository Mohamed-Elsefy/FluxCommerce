using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.AspNetCore.Authorization;

namespace FluxCommerce.Api.Authorization;

/// <summary>
/// Evaluates <see cref="PermissionRequirement"/>: a <c>SuperAdmin</c> role claim succeeds any
/// permission requirement immediately (supremacy); otherwise the matching <c>permission</c>
/// claim (see <see cref="PermissionCatalog.ClaimType"/>) must be present.
/// </summary>
public sealed class PermissionOrSuperAdminHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.User.IsInRole(Roles.SuperAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.User.HasClaim(PermissionCatalog.ClaimType, requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
