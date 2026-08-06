using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class CatalogAuthorizationTests : CatalogApiTestBase
{
    public CatalogAuthorizationTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Theory]
    [InlineData("catalog/categories")]
    [InlineData("catalog/products")]
    [InlineData("catalog/attribute-definitions")]
    public async Task WriteEndpoints_WithoutToken_ReturnsUnauthorized(string endpoint)
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            endpoint,
            new { name = "Unauthorized", slug = "unauthorized", description = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("catalog/products")]
    [InlineData("catalog/categories")]
    [InlineData("catalog/attribute-definitions")]
    public async Task WriteEndpoints_WithoutToken_PutReturnsUnauthorized(string endpoint)
    {
        var client = Factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"{endpoint}/{Guid.NewGuid()}",
            new { name = "Unauthorized", slug = "unauthorized", description = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublicReadEndpoints_WorkWithoutToken()
    {
        var client = Factory.CreateClient();

        var categories = await client.GetAsync("catalog/categories");
        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);

        var products = await client.GetAsync("catalog/products");
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);

        var search = await client.GetAsync("catalog");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
    }

    [Fact]
    public async Task WriteEndpoints_WithToken_AreAuthorized()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Authorized Category", slug = "authorized-category", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetProduct_WithDeletedProduct_RequiresNoToken_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"catalog/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
