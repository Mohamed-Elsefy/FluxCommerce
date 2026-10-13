using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class CatalogAdminGatingTests : CatalogApiTestBase
{
    public CatalogAdminGatingTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Customer_Cannot_Create_Category_Forbidden()
    {
        var client = Factory.CreateClient();
        var jwtProvider = Factory.Services.GetRequiredService<IJwtProvider>();
        var customerToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "customer@flux.local", ["Customer"]);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", customerToken);

        var response = await client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Forbidden Category", slug = "forbidden-category", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_Cannot_Create_Product_Forbidden()
    {
        var client = Factory.CreateClient();
        var jwtProvider = Factory.Services.GetRequiredService<IJwtProvider>();
        var customerToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "customer@flux.local", ["Customer"]);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", customerToken);

        var categoryId = await CreateCategoryAsAdminAsync();
        var response = await client.PostAsJsonAsync(
            "catalog/products",
            new { name = "Forbidden Product", slug = "forbidden-product", description = (string?)null, categoryId, visibility = 0 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Create_Category_Success()
    {
        var client = Factory.CreateClient();
        var jwtProvider = Factory.Services.GetRequiredService<IJwtProvider>();
        var adminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "admin@flux.local", ["Admin"], [PermissionCatalog.CatalogManage]);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Admin Category", slug = $"admin-category-{Guid.NewGuid():N}", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Create_Product_Success()
    {
        var client = Factory.CreateClient();
        var jwtProvider = Factory.Services.GetRequiredService<IJwtProvider>();
        var adminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "admin@flux.local", ["Admin"], [PermissionCatalog.CatalogManage]);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        // Need a category first (as Admin)
        var categoryResponse = await client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Admin Cat For Product", slug = $"admin-cat-prod-{Guid.NewGuid():N}", description = (string?)null, parentId = (Guid?)null });
        categoryResponse.EnsureSuccessStatusCode();
        var categoryId = (await categoryResponse.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;

        var response = await client.PostAsJsonAsync(
            "catalog/products",
            new { name = "Admin Product", slug = $"admin-product-{Guid.NewGuid():N}", description = (string?)null, categoryId, visibility = 0 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithoutPermission_Cannot_Create_Category_Forbidden()
    {
        // Phase D: the Admin role alone no longer opens permission-gated endpoints.
        var client = Factory.CreateClient();
        var jwtProvider = Factory.Services.GetRequiredService<IJwtProvider>();
        var adminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "admin@flux.local", ["Admin"]);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "No Permission Category", slug = $"noperm-{Guid.NewGuid():N}", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithUnrelatedPermission_Cannot_Create_Category_Forbidden()
    {
        var client = Factory.CreateClient();
        var jwtProvider = Factory.Services.GetRequiredService<IJwtProvider>();
        var adminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "admin@flux.local", ["Admin"], [PermissionCatalog.OrdersRead]);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Wrong Permission Category", slug = $"wrongperm-{Guid.NewGuid():N}", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreateCategoryAsAdminAsync()
    {
        var client = Factory.CreateClient();
        var jwtProvider = Factory.Services.GetRequiredService<IJwtProvider>();
        var adminToken = jwtProvider.CreateAccessToken(Guid.NewGuid(), "admin2@flux.local", ["Admin"], [PermissionCatalog.CatalogManage]);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Tmp Category", slug = $"tmp-{Guid.NewGuid():N}", description = (string?)null, parentId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
    }
}
