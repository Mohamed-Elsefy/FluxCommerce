using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.AspNetCore.Authorization;

namespace FluxCommerce.Api.Authorization;

/// <summary>
/// Evaluates <see cref="RoleOrSuperAdminRequirement"/>: a <c>SuperAdmin</c> role claim succeeds
/// any requirement immediately (supremacy); otherwise the specifically required role claim must
/// be present. The <c>SuperAdminPolicy</c> itself keeps exact <c>RequireRole</c> semantics and
/// never flows through this handler.
/// </summary>
public sealed class RoleOrSuperAdminHandler : AuthorizationHandler<RoleOrSuperAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleOrSuperAdminRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.User.IsInRole(Roles.SuperAdmin) || context.User.IsInRole(requirement.Role))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
