using System.Net;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class CategoriesTests : CatalogApiTestBase
{
    public CategoriesTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithIdAndLocationHeader()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Electronics", slug = "electronics", description = "Devices", parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var data = (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
        Assert.NotEqual(Guid.Empty, data);
        Assert.Contains(data.ToString(), response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Create_SetsStatusActive()
    {
        var categoryId = await CreateCategoryAsync("Books");

        var get = await Client.GetAsync($"catalog/categories/{categoryId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<CategoryResponse>>()).Data!;
        Assert.Equal(CategoryStatus.Active, data.Status);
        Assert.False(data.IsDeleted);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await AuthorizeAsync();
        await CreateCategoryAsync("Unique Name", "unique-name");

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Unique Name", slug = "other-slug", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A category with this name already exists.", problem.Detail);
    }

    [Fact]
    public async Task Create_DuplicateSlug_ReturnsConflict()
    {
        await AuthorizeAsync();
        await CreateCategoryAsync("First Category", "shared-slug");

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Second Category", slug = "shared-slug", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A category with this slug already exists.", problem.Detail);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsBadRequest()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = string.Empty, slug = "empty-name", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }

    [Fact]
    public async Task Create_InvalidSlug_ReturnsBadRequest()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Invalid Slug", slug = "invalid slug!", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The slug is invalid.", problem.Detail);
    }

    [Fact]
    public async Task Create_WithParent_SetsParent()
    {
        var parentId = await CreateCategoryAsync("Parent Category", "parent-category");
        var childId = await CreateCategoryAsync("Child Category", "child-category", parentId: parentId);

        var get = await Client.GetAsync($"catalog/categories/{childId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<CategoryResponse>>()).Data!;
        Assert.Equal(parentId, data.ParentId);
    }

    [Fact]
    public async Task Create_WithNonExistentParent_ReturnsNotFound()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Orphan", slug = "orphan", description = (string?)null, parentId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The parent category was not found.", problem.Detail);
    }

    [Fact]
    public async Task Create_WithDeletedParent_ReturnsConflict()
    {
        var parentId = await CreateCategoryAsync("Going Away", "going-away");
        await SoftDeleteCategoryAsync(parentId);

        var response = await Client.PostAsJsonAsync(
            "catalog/categories",
            new { name = "Child", slug = "child", description = (string?)null, parentId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The parent category is deleted and cannot be used.", problem.Detail);
    }

    [Fact]
    public async Task Update_UpdatesFields()
    {
        var categoryId = await CreateCategoryAsync("Original Name", "original-name");

        var response = await Client.PutAsJsonAsync(
            $"catalog/categories/{categoryId}",
            new { name = "Updated Name", slug = "updated-name", description = "Updated description", parentId = (Guid?)null });
        response.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var get = await Client.GetAsync($"catalog/categories/{categoryId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<CategoryResponse>>()).Data!;
        Assert.Equal("Updated Name", data.Name);
        Assert.Equal("updated-name", data.Slug);
        Assert.Equal("Updated description", data.Description);
    }

    [Fact]
    public async Task Update_NonExistentCategory_ReturnsNotFound()
    {
        await AuthorizeAsync();

        var response = await Client.PutAsJsonAsync(
            $"catalog/categories/{Guid.NewGuid()}",
            new { name = "Nope", slug = "nope", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsConflict()
    {
        await CreateCategoryAsync("Taken", "taken");
        var categoryId = await CreateCategoryAsync("Free", "free");

        var response = await Client.PutAsJsonAsync(
            $"catalog/categories/{categoryId}",
            new { name = "Taken", slug = "free", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A category with this name already exists.", problem.Detail);
    }

    [Fact]
    public async Task Update_SelfParent_ReturnsConflict()
    {
        var categoryId = await CreateCategoryAsync("Self Referential", "self-referential");

        var response = await Client.PutAsJsonAsync(
            $"catalog/categories/{categoryId}",
            new { name = "Self Referential", slug = "self-referential", description = (string?)null, parentId = categoryId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A category cannot be its own parent.", problem.Detail);
    }

    [Fact]
    public async Task Update_DeletedCategory_ReturnsConflict()
    {
        var categoryId = await CreateCategoryAsync("To Be Deleted", "to-be-deleted");
        await SoftDeleteCategoryAsync(categoryId);

        var response = await Client.PutAsJsonAsync(
            $"catalog/categories/{categoryId}",
            new { name = "Changed", slug = "changed", description = (string?)null, parentId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A deleted category cannot be modified.", problem.Detail);
    }

    [Fact]
    public async Task Deactivate_SetsStatusInactive()
    {
        var categoryId = await CreateCategoryAsync("Sleepy", "sleepy");

        var response = await Client.PostAsync($"catalog/categories/{categoryId}/deactivate", null);
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/categories/{categoryId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<CategoryResponse>>()).Data!;
        Assert.Equal(CategoryStatus.Inactive, data.Status);
    }

    [Fact]
    public async Task Activate_AfterDeactivate_SetsStatusActive()
    {
        var categoryId = await CreateCategoryAsync("Wake Up", "wake-up");
        var deactivate = await Client.PostAsync($"catalog/categories/{categoryId}/deactivate", null);
        deactivate.EnsureSuccessStatusCode();

        var response = await Client.PostAsync($"catalog/categories/{categoryId}/activate", null);
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/categories/{categoryId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<CategoryResponse>>()).Data!;
        Assert.Equal(CategoryStatus.Active, data.Status);
    }

    [Fact]
    public async Task SoftDelete_MarksDeleted()
    {
        var categoryId = await CreateCategoryAsync("Expiring", "expiring");

        var response = await Client.PostAsync($"catalog/categories/{categoryId}/soft-delete", null);
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/categories/{categoryId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<CategoryResponse>>()).Data!;
        Assert.True(data.IsDeleted);
        Assert.NotNull(data.DeletedAtUtc);
    }

    [Fact]
    public async Task Restore_UnmarksDeleted()
    {
        var categoryId = await CreateCategoryAsync("Back Again", "back-again");
        await SoftDeleteCategoryAsync(categoryId);

        var response = await Client.PostAsync($"catalog/categories/{categoryId}/restore", null);
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/categories/{categoryId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<CategoryResponse>>()).Data!;
        Assert.False(data.IsDeleted);
        Assert.Null(data.DeletedAtUtc);
    }

    [Fact]
    public async Task Get_NonExistentCategory_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"catalog/categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task List_ReturnsOnlyNonDeletedCategories()
    {
        var keepId = await CreateCategoryAsync("Keep Me", "keep-me");
        var deleteId = await CreateCategoryAsync("Delete Me", "delete-me");
        await SoftDeleteCategoryAsync(deleteId);

        var response = await Client.GetAsync("catalog/categories");
        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CategoryResponse>>>()).Data!;
        Assert.Contains(data.Items, category => category.Id == keepId);
        Assert.DoesNotContain(data.Items, category => category.Id == deleteId);
    }

    [Fact]
    public async Task List_Paginates()
    {
        await CreateCategoryAsync("List Cat One", "list-cat-one");
        await CreateCategoryAsync("List Cat Two", "list-cat-two");
        await CreateCategoryAsync("List Cat Three", "list-cat-three");

        var first = await Client.GetAsync("catalog/categories?pageSize=2&pageNumber=1");
        first.EnsureSuccessStatusCode();
        var firstData = (await first.ReadJsonAsync<ApiResponseDto<PagedResult<CategoryResponse>>>()).Data!;
        Assert.Equal(3, firstData.TotalCount);
        Assert.Equal(2, firstData.Items.Count);
        Assert.Equal(1, firstData.PageNumber);

        var second = await Client.GetAsync("catalog/categories?pageSize=2&pageNumber=2");
        second.EnsureSuccessStatusCode();
        var secondData = (await second.ReadJsonAsync<ApiResponseDto<PagedResult<CategoryResponse>>>()).Data!;
        Assert.Single(secondData.Items);
        Assert.Equal(2, secondData.PageNumber);
    }

    [Fact]
    public async Task List_InvalidPageSize_ReturnsBadRequest()
    {
        var response = await Client.GetAsync("catalog/categories?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }

    private async Task SoftDeleteCategoryAsync(Guid categoryId)
    {
        var response = await Client.PostAsync($"catalog/categories/{categoryId}/soft-delete", null);
        response.EnsureSuccessStatusCode();
    }
}
