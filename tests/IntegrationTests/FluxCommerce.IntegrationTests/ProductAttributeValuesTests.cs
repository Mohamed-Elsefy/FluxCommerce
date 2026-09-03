using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class ProductAttributeValuesTests : CatalogApiTestBase
{
    public ProductAttributeValuesTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Assign_ReturnsNoContent()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync();

        var response = await AssignValueAsync(productId, attributeId, "Red");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Assign_ThenGet_ReturnsValue()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync();
        await AssignValueAsync(productId, attributeId, "Red");

        var get = await Client.GetAsync($"catalog/products/{productId}/attributes/{attributeId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<ProductAttributeValueResponse>>()).Data!;
        Assert.Equal(productId, data.ProductId);
        Assert.Equal(attributeId, data.AttributeDefinitionId);
        Assert.Equal("Red", data.Value);
        Assert.Equal(AttributeDataType.Text, data.DataType);
    }

    [Fact]
    public async Task Assign_NonExistentProduct_ReturnsNotFound()
    {
        var attributeId = await CreateAttributeDefinitionAsync("Standalone", "standalone", AttributeDataType.Text);

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{Guid.NewGuid()}/attributes",
            new { attributeDefinitionId = attributeId, value = "X" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task Assign_NonExistentDefinition_ReturnsNotFound()
    {
        var (productId, _) = await CreateProductAndAttributeAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/attributes",
            new { attributeDefinitionId = Guid.NewGuid(), value = "X" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The attribute definition was not found.", problem.Detail);
    }

    [Fact]
    public async Task Assign_Duplicate_ReturnsConflict()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync();
        await AssignValueAsync(productId, attributeId, "Red");

        var response = await AssignValueAsync(productId, attributeId, "Blue");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The product already has a value for the specified attribute.", problem.Detail);
    }

    [Fact]
    public async Task Assign_InvalidValueForNumberType_ReturnsConflict()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync(AttributeDataType.Number);

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/attributes",
            new { attributeDefinitionId = attributeId, value = "not-a-number" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The attribute value is not valid for the attribute's data type.", problem.Detail);
    }

    [Fact]
    public async Task Assign_WithInactiveDefinition_ReturnsConflict()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync();
        var deactivate = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/deactivate", null);
        deactivate.EnsureSuccessStatusCode();

        var response = await AssignValueAsync(productId, attributeId, "Red");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The attribute definition is not active and cannot be used.", problem.Detail);
    }

    [Fact]
    public async Task Assign_WithDeletedDefinition_ReturnsConflict()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync();
        var softDelete = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/soft-delete", null);
        softDelete.EnsureSuccessStatusCode();

        var response = await AssignValueAsync(productId, attributeId, "Red");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The attribute definition is deleted and cannot be used.", problem.Detail);
    }

    [Fact]
    public async Task Assign_WithCategoryMismatch_ReturnsConflict()
    {
        var firstCategoryId = await CreateCategoryAsync("First Scope", "first-scope");
        var secondCategoryId = await CreateCategoryAsync("Second Scope", "second-scope");
        var productId = await CreateProductAsync(firstCategoryId, "Scoped Product");
        var attributeId = await CreateAttributeDefinitionAsync("Scoped Attr", "scoped-attr", AttributeDataType.Text, secondCategoryId);

        var response = await AssignValueAsync(productId, attributeId, "Red");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The attribute definition does not belong to the product's category.", problem.Detail);
    }

    [Fact]
    public async Task Update_UpdatesValue()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync();
        await AssignValueAsync(productId, attributeId, "Red");

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}/attributes/{attributeId}",
            new { value = "Blue" });
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/products/{productId}/attributes/{attributeId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<ProductAttributeValueResponse>>()).Data!;
        Assert.Equal("Blue", data.Value);
    }

    [Fact]
    public async Task Update_NonExistentValue_ReturnsConflict()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync();

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}/attributes/{attributeId}",
            new { value = "Blue" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The product does not have a value for the specified attribute.", problem.Detail);
    }

    [Fact]
    public async Task Remove_RemovesValue()
    {
        var (productId, attributeId) = await CreateProductAndAttributeAsync();
        await AssignValueAsync(productId, attributeId, "Red");

        var response = await Client.DeleteAsync($"catalog/products/{productId}/attributes/{attributeId}");
        response.EnsureSuccessStatusCode();

        var list = await GetValuesAsync(productId);
        Assert.Empty(list);
    }

    [Fact]
    public async Task List_ReturnsAssignedValues()
    {
        var (productId, firstAttributeId) = await CreateProductAndAttributeAsync();
        var secondAttributeId = await CreateAttributeDefinitionAsync("Another Attr", "another-attr", AttributeDataType.Text);
        await AssignValueAsync(productId, firstAttributeId, "Red");
        await AssignValueAsync(productId, secondAttributeId, "Blue");

        var list = await GetValuesAsync(productId);

        Assert.Equal(2, list.Count);
    }

    private async Task<(Guid ProductId, Guid AttributeId)> CreateProductAndAttributeAsync(AttributeDataType dataType = AttributeDataType.Text)
    {
        var categoryId = await CreateCategoryAsync("Attribute Products", "attribute-products");
        var productId = await CreateProductAsync(categoryId, "Attribute Product");
        var attributeId = await CreateAttributeDefinitionAsync("Color", "color", dataType);
        return (productId, attributeId);
    }

    private async Task<Guid> CreateAttributeDefinitionAsync(string name, string slug, AttributeDataType dataType, Guid? categoryId = null)
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/attribute-definitions",
            new { name, slug, description = (string?)null, dataType, isRequired = false, isFilterable = false, isSearchable = false, sortOrder = 0, categoryId });
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
    }

    private async Task<HttpResponseMessage> AssignValueAsync(Guid productId, Guid attributeId, string value) =>
        await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/attributes",
            new { attributeDefinitionId = attributeId, value });

    private async Task<IReadOnlyCollection<ProductAttributeValueResponse>> GetValuesAsync(Guid productId)
    {
        var response = await Client.GetAsync($"catalog/products/{productId}/attributes");
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<ProductAttributeValueResponse>>>()).Data!;
    }
}
