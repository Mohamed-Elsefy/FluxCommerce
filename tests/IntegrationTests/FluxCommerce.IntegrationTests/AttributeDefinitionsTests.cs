using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class AttributeDefinitionsTests : CatalogApiTestBase
{
    public AttributeDefinitionsTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithId()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/attribute-definitions",
            BuildCreateBody(name: "Color", slug: "color", dataType: AttributeDataType.Text));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
        Assert.NotEqual(Guid.Empty, data);
    }

    [Fact]
    public async Task Create_DefaultsToActiveStatus()
    {
        var attributeId = await CreateAttributeDefinitionAsync("Size", "size", AttributeDataType.Text);

        var get = await Client.GetAsync($"catalog/attribute-definitions/{attributeId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<AttributeDefinitionResponse>>()).Data!;
        Assert.Equal(AttributeStatus.Active, data.Status);
        Assert.False(data.IsDeleted);
        Assert.Equal(AttributeDataType.Text, data.DataType);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await AuthorizeAsync();
        await CreateAttributeDefinitionAsync("Material", "material", AttributeDataType.Text);

        var response = await Client.PostAsJsonAsync(
            "catalog/attribute-definitions",
            BuildCreateBody(name: "Material", slug: "material-2", dataType: AttributeDataType.Text));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("An attribute definition with this name already exists.", problem.Detail);
    }

    [Fact]
    public async Task Create_DuplicateSlug_ReturnsConflict()
    {
        await AuthorizeAsync();
        await CreateAttributeDefinitionAsync("Weight", "weight", AttributeDataType.Decimal);

        var response = await Client.PostAsJsonAsync(
            "catalog/attribute-definitions",
            BuildCreateBody(name: "Mass", slug: "weight", dataType: AttributeDataType.Decimal));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("An attribute definition with this slug already exists.", problem.Detail);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsBadRequest()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/attribute-definitions",
            BuildCreateBody(name: string.Empty, slug: "empty", dataType: AttributeDataType.Text));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }

    [Fact]
    public async Task Create_InvalidSlug_ReturnsBadRequest()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/attribute-definitions",
            BuildCreateBody(name: "Broken", slug: "broken slug!", dataType: AttributeDataType.Text));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The slug is invalid.", problem.Detail);
    }

    [Fact]
    public async Task Create_WithNonExistentCategory_ReturnsNotFound()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/attribute-definitions",
            new
            {
                name = "Scoped",
                slug = "scoped",
                description = (string?)null,
                dataType = AttributeDataType.Text,
                isRequired = false,
                isFilterable = false,
                isSearchable = false,
                sortOrder = 0,
                categoryId = Guid.NewGuid()
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task Create_WithCategory_SetsCategory()
    {
        var categoryId = await CreateCategoryAsync("Attribute Scope", "attribute-scope");
        var attributeId = await CreateAttributeDefinitionAsync("Scoped Color", "scoped-color", AttributeDataType.Text, categoryId);

        var get = await Client.GetAsync($"catalog/attribute-definitions/{attributeId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<AttributeDefinitionResponse>>()).Data!;
        Assert.Equal(categoryId, data.CategoryId);
    }

    [Fact]
    public async Task Update_UpdatesFields()
    {
        var attributeId = await CreateAttributeDefinitionAsync("Old Name", "old-name", AttributeDataType.Text);

        var response = await Client.PutAsJsonAsync(
            $"catalog/attribute-definitions/{attributeId}",
            BuildCreateBody(name: "New Name", slug: "new-name", dataType: AttributeDataType.Number));
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/attribute-definitions/{attributeId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<AttributeDefinitionResponse>>()).Data!;
        Assert.Equal("New Name", data.Name);
        Assert.Equal("new-name", data.Slug);
        Assert.Equal(AttributeDataType.Number, data.DataType);
    }

    [Fact]
    public async Task Update_NonExistent_ReturnsNotFound()
    {
        await AuthorizeAsync();

        var response = await Client.PutAsJsonAsync(
            $"catalog/attribute-definitions/{Guid.NewGuid()}",
            BuildCreateBody(name: "Nope", slug: "nope", dataType: AttributeDataType.Text));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The attribute definition was not found.", problem.Detail);
    }

    [Fact]
    public async Task Deactivate_SetsInactive()
    {
        var attributeId = await CreateAttributeDefinitionAsync("Sleepy Attr", "sleepy-attr", AttributeDataType.Text);

        var response = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/deactivate", null);
        response.EnsureSuccessStatusCode();

        var data = await GetAttributeDefinitionAsync(attributeId);
        Assert.Equal(AttributeStatus.Inactive, data.Status);
    }

    [Fact]
    public async Task Activate_AfterDeactivate_Activates()
    {
        var attributeId = await CreateAttributeDefinitionAsync("Wakey Attr", "wakey-attr", AttributeDataType.Text);
        var deactivate = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/deactivate", null);
        deactivate.EnsureSuccessStatusCode();

        var response = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/activate", null);
        response.EnsureSuccessStatusCode();

        var data = await GetAttributeDefinitionAsync(attributeId);
        Assert.Equal(AttributeStatus.Active, data.Status);
    }

    [Fact]
    public async Task SoftDelete_MarksDeleted()
    {
        var attributeId = await CreateAttributeDefinitionAsync("Expiring Attr", "expiring-attr", AttributeDataType.Text);

        var response = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/soft-delete", null);
        response.EnsureSuccessStatusCode();

        var data = await GetAttributeDefinitionAsync(attributeId);
        Assert.True(data.IsDeleted);
        Assert.NotNull(data.DeletedAtUtc);
    }

    [Fact]
    public async Task Restore_UnmarksDeleted()
    {
        var attributeId = await CreateAttributeDefinitionAsync("Restoring Attr", "restoring-attr", AttributeDataType.Text);
        var softDelete = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/soft-delete", null);
        softDelete.EnsureSuccessStatusCode();

        var response = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/restore", null);
        response.EnsureSuccessStatusCode();

        var data = await GetAttributeDefinitionAsync(attributeId);
        Assert.False(data.IsDeleted);
        Assert.Null(data.DeletedAtUtc);
    }

    [Fact]
    public async Task Update_Deleted_ReturnsConflict()
    {
        var attributeId = await CreateAttributeDefinitionAsync("Gone Attr", "gone-attr", AttributeDataType.Text);
        var softDelete = await Client.PostAsync($"catalog/attribute-definitions/{attributeId}/soft-delete", null);
        softDelete.EnsureSuccessStatusCode();

        var response = await Client.PutAsJsonAsync(
            $"catalog/attribute-definitions/{attributeId}",
            BuildCreateBody(name: "Changed", slug: "changed", dataType: AttributeDataType.Text));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A deleted attribute definition cannot be modified.", problem.Detail);
    }

    [Fact]
    public async Task List_ReturnsOnlyNonDeleted()
    {
        var keepId = await CreateAttributeDefinitionAsync("Keep Attr", "keep-attr", AttributeDataType.Text);
        var deleteId = await CreateAttributeDefinitionAsync("Delete Attr", "delete-attr", AttributeDataType.Text);
        var softDelete = await Client.PostAsync($"catalog/attribute-definitions/{deleteId}/soft-delete", null);
        softDelete.EnsureSuccessStatusCode();

        var response = await Client.GetAsync("catalog/attribute-definitions");
        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<AttributeDefinitionResponse>>>()).Data!;
        Assert.Contains(data, attribute => attribute.Id == keepId);
        Assert.DoesNotContain(data, attribute => attribute.Id == deleteId);
    }

    private async Task<Guid> CreateAttributeDefinitionAsync(string name, string slug, AttributeDataType dataType, Guid? categoryId = null)
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/attribute-definitions",
            BuildCreateBody(name, slug, dataType, categoryId));
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
    }

    private async Task<AttributeDefinitionResponse> GetAttributeDefinitionAsync(Guid attributeId)
    {
        var response = await Client.GetAsync($"catalog/attribute-definitions/{attributeId}");
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<AttributeDefinitionResponse>>()).Data!;
    }

    private static object BuildCreateBody(string name, string slug, AttributeDataType dataType, Guid? categoryId = null) =>
        new
        {
            name,
            slug,
            description = (string?)null,
            dataType,
            isRequired = false,
            isFilterable = false,
            isSearchable = false,
            sortOrder = 0,
            categoryId
        };
}
