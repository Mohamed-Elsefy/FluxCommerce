using System.Net;
using System.Text.Json;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public class SwaggerSecurityTests
{
    private readonly IdentityWebApplicationFactory _factory;

    public SwaggerSecurityTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublicEndpoints_DoNotRequireAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        AssertEmptySecurity(json, "/catalog", "get");
        AssertEmptySecurity(json, "/catalog/products", "get");
        AssertEmptySecurity(json, "/catalog/categories", "get");
        AssertEmptySecurity(json, "/auth/login", "post");
    }

    [Fact]
    public async Task ProtectedEndpoints_RequireAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var rootSecurity = json.RootElement.GetProperty("security");
        var requirement = Assert.Single(rootSecurity.EnumerateArray());
        Assert.True(requirement.TryGetProperty("Bearer", out var scopes));
        Assert.Equal(0, scopes.GetArrayLength());
    }

    [Fact]
    public async Task ProtectedCatalogEndpoints_DoNotOverrideAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var createProduct = json.RootElement
            .GetProperty("paths")
            .GetProperty("/catalog/products")
            .GetProperty("post");

        Assert.False(createProduct.TryGetProperty("security", out _));
    }

    private static void AssertEmptySecurity(JsonDocument json, string path, string method)
    {
        var operation = json.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method);

        Assert.True(operation.TryGetProperty("security", out var security));
        Assert.Equal(0, security.GetArrayLength());
    }
}
