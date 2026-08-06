using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class ProductSpecificationsTests : CatalogApiTestBase
{
    public ProductSpecificationsTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_ReturnsNoContent()
    {
        var productId = await CreateProductForSpecificationsAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/specifications",
            new { name = "Warranty", value = "2 years" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        var productId = await CreateProductForSpecificationsAsync();
        await CreateSpecificationAsync(productId, "Weight", "1kg");

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/specifications",
            new { name = "weight", value = "2kg" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The product already has a specification with this name.", problem.Detail);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsBadRequest()
    {
        var productId = await CreateProductForSpecificationsAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/specifications",
            new { name = string.Empty, value = "2 years" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }

    [Fact]
    public async Task Create_NonExistentProduct_ReturnsNotFound()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{Guid.NewGuid()}/specifications",
            new { name = "Warranty", value = "2 years" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task Update_UpdatesNameAndValue()
    {
        var productId = await CreateProductForSpecificationsAsync();
        var specificationId = await CreateSpecificationAsync(productId, "Warranty", "2 years");

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}/specifications/{specificationId}",
            new { name = "Guarantee", value = "3 years" });
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/products/{productId}/specifications/{specificationId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<ProductSpecificationResponse>>()).Data!;
        Assert.Equal("Guarantee", data.Name);
        Assert.Equal("3 years", data.Value);
    }

    [Fact]
    public async Task Update_NonExistent_ReturnsConflict()
    {
        var productId = await CreateProductForSpecificationsAsync();

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}/specifications/{Guid.NewGuid()}",
            new { name = "Guarantee", value = "3 years" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The product does not have the specified specification.", problem.Detail);
    }

    [Fact]
    public async Task Delete_RemovesSpecification()
    {
        var productId = await CreateProductForSpecificationsAsync();
        var specificationId = await CreateSpecificationAsync(productId, "Warranty", "2 years");

        var response = await Client.DeleteAsync($"catalog/products/{productId}/specifications/{specificationId}");
        response.EnsureSuccessStatusCode();

        var list = await GetSpecificationsAsync(productId);
        Assert.Empty(list);
    }

    [Fact]
    public async Task List_ReturnsAllSpecifications()
    {
        var productId = await CreateProductForSpecificationsAsync();
        await CreateSpecificationAsync(productId, "Warranty", "2 years");
        await CreateSpecificationAsync(productId, "Weight", "1kg");

        var list = await GetSpecificationsAsync(productId);

        Assert.Equal(2, list.Count);
    }

    private async Task<Guid> CreateProductForSpecificationsAsync()
    {
        var categoryId = await CreateCategoryAsync("Specification Products", "specification-products");
        return await CreateProductAsync(categoryId, "Specified Product");
    }

    private async Task<Guid> CreateSpecificationAsync(Guid productId, string name, string value)
    {
        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/specifications",
            new { name, value });
        response.EnsureSuccessStatusCode();

        return (await GetSpecificationsAsync(productId))
            .Single(specification => specification.Name == name)
            .Id;
    }

    private async Task<IReadOnlyCollection<ProductSpecificationResponse>> GetSpecificationsAsync(Guid productId)
    {
        var response = await Client.GetAsync($"catalog/products/{productId}/specifications");
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<ProductSpecificationResponse>>>()).Data!;
    }
}
