using System.Net;
using System.Net.Http.Headers;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Covers SuperAdmin supremacy and permission enforcement (ADR-016 D1/D2, Phases A+D):
/// a SuperAdmin passes permission-gated endpoints without any permission claim, an Admin
/// passes only with the matching claim, and read/manage permissions do not imply each other.
/// </summary>
[Collection(InventoryApiTestCollection.Name)]
public class SuperAdminSupremacyTests
{
    private readonly InventoryWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SuperAdminSupremacyTests(InventoryWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SuperAdmin_PassesPermissionGatedEndpoint_WithoutClaims()
    {
        AuthorizeAs(["SuperAdmin"], []);

        // Unknown SKU: 404 proves the permission gate passed (403 would mean rejection).
        var response = await _client.GetAsync("inventory/SKU-SUPREMACY-PROBE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithMatchingPermission_PassesPermissionGatedEndpoint()
    {
        AuthorizeAs(["Admin"], [PermissionCatalog.InventoryRead]);

        var response = await _client.GetAsync("inventory/SKU-SUPREMACY-PROBE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithoutPermission_RejectedFromPermissionGatedEndpoint()
    {
        // Phase D: the Admin role alone no longer opens permission-gated endpoints.
        AuthorizeAs(["Admin"], []);

        var response = await _client.GetAsync("inventory/SKU-SUPREMACY-PROBE");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithUnrelatedPermission_RejectedFromPermissionGatedEndpoint()
    {
        AuthorizeAs(["Admin"], [PermissionCatalog.OrdersRead]);

        var response = await _client.GetAsync("inventory/SKU-SUPREMACY-PROBE");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InventoryRead_DoesNotGrantManage()
    {
        AuthorizeAs(["Admin"], [PermissionCatalog.InventoryRead]);

        var read = await _client.GetAsync("inventory/SKU-SUPREMACY-PROBE");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var write = await _client.PostAsJsonAsync(
            "inventory",
            new { sku = "SKU-SUPREMACY-WRITE", onHandQuantity = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Customer_StillRejectedFromPermissionGatedEndpoint()
    {
        AuthorizeAs(["Customer"], []);

        var response = await _client.GetAsync("inventory/SKU-SUPREMACY-PROBE");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_StillRejectedFromSuperAdminPolicyEndpoint()
    {
        AuthorizeAs(["Admin"], PermissionCatalog.AdminBundle);

        var response = await _client.PostAsJsonAsync(
            $"admin/users/{Guid.NewGuid()}/roles",
            new { role = "Admin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_PassesSuperAdminPolicyEndpoint()
    {
        AuthorizeAs(["SuperAdmin"], []);

        // Unknown user: 404 proves the SuperAdminPolicy gate passed (403 would mean rejection).
        var response = await _client.PostAsJsonAsync(
            $"admin/users/{Guid.NewGuid()}/roles",
            new { role = "Admin" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private void AuthorizeAs(IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var jwtProvider = _factory.Services.GetRequiredService<IJwtProvider>();
        var token = jwtProvider.CreateAccessToken(Guid.NewGuid(), "supremacy@flux.local", roles, permissions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
