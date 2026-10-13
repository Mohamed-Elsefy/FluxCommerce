using System.Security.Claims;
using FluxCommerce.Api.Authorization;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace FluxCommerce.UnitTests.Authorization;

public sealed class RoleOrSuperAdminHandlerTests
{
    private readonly RoleOrSuperAdminHandler _handler = new();

    [Fact]
    public async Task HandleAsync_SuperAdmin_SucceedsAnyRoleRequirement()
    {
        var requirement = new RoleOrSuperAdminRequirement(Roles.Admin);
        var context = CreateContext(["SuperAdmin"], requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_MatchingRole_Succeeds()
    {
        var requirement = new RoleOrSuperAdminRequirement(Roles.Admin);
        var context = CreateContext(["Admin"], requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_OtherRole_DoesNotSucceed()
    {
        var requirement = new RoleOrSuperAdminRequirement(Roles.Admin);
        var context = CreateContext(["Customer"], requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_DoesNotSucceed()
    {
        var requirement = new RoleOrSuperAdminRequirement(Roles.Admin);
        var context = CreateContext([], requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_CustomerRequirement_SatisfiedByCustomerRole()
    {
        var requirement = new RoleOrSuperAdminRequirement(Roles.Customer);
        var context = CreateContext(["Customer"], requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CreateContext(
        IReadOnlyCollection<string> roles,
        IAuthorizationRequirement requirement)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return new AuthorizationHandlerContext([requirement], principal, null);
    }
}
