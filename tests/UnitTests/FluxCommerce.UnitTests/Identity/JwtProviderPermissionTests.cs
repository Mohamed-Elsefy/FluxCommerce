using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.Authentication;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Identity;

public sealed class JwtProviderPermissionTests
{
    private static JwtProvider CreateProvider() =>
        new(
            Options.Create(new JwtOptions
            {
                Issuer = "https://localhost:7119",
                Audience = "https://localhost:7119",
                SigningKey = "test-signing-key-that-is-long-enough-32",
                AccessTokenLifetimeMinutes = 15,
                ClockSkewMinutes = 5,
            }),
            new FakeDateTimeProvider());

    private static IReadOnlyCollection<string> ReadPermissions(string token) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(token)
            .Claims
            .Where(claim => claim.Type == PermissionCatalog.ClaimType)
            .Select(claim => claim.Value)
            .ToList();

    [Fact]
    public void CreateAccessToken_WithPermissions_EmitsOneClaimPerPermission()
    {
        var provider = CreateProvider();

        var token = provider.CreateAccessToken(
            Guid.NewGuid(),
            "admin@flux.local",
            [Roles.Admin],
            [PermissionCatalog.OrdersRead, PermissionCatalog.InventoryManage]);

        var permissions = ReadPermissions(token);
        Assert.Equal(2, permissions.Count);
        Assert.Contains(PermissionCatalog.OrdersRead, permissions);
        Assert.Contains(PermissionCatalog.InventoryManage, permissions);
    }

    [Fact]
    public void CreateAccessToken_WithoutPermissions_EmitsNoPermissionClaims()
    {
        var provider = CreateProvider();

        var token = provider.CreateAccessToken(Guid.NewGuid(), "user@flux.local", [Roles.Customer]);

        Assert.Empty(ReadPermissions(token));
    }

    [Fact]
    public void CreateAccessToken_KeepsRoleClaimsAlongsidePermissions()
    {
        var provider = CreateProvider();

        var token = provider.CreateAccessToken(
            Guid.NewGuid(),
            "admin@flux.local",
            [Roles.Admin],
            [PermissionCatalog.DashboardRead]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == Roles.Admin);
        Assert.Contains(jwt.Claims, claim => claim.Type == PermissionCatalog.ClaimType && claim.Value == PermissionCatalog.DashboardRead);
    }
}
