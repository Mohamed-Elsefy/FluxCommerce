using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class ProductsTests : CatalogApiTestBase
{
    public ProductsTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithId()
    {
        await AuthorizeAsync();
        var categoryId = await CreateCategoryAsync("Gadgets", "gadgets");

        var response = await Client.PostAsJsonAsync(
            "catalog/products",
            new { name = "Widget", slug = "widget", description = (string?)null, categoryId, visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
        Assert.NotEqual(Guid.Empty, data);
    }

    [Fact]
    public async Task Create_DefaultsToDraftAndVisible()
    {
        var categoryId = await CreateCategoryAsync("Basics", "basics");
        var productId = await CreateProductAsync(categoryId, "Draft Product");

        var get = await Client.GetAsync($"catalog/products/{productId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<ProductResponse>>()).Data!;
        Assert.Equal(ProductStatus.Draft, data.Status);
        Assert.Equal(ProductVisibility.Visible, data.Visibility);
        Assert.False(data.IsDeleted);
    }

    [Fact]
    public async Task Create_WithHiddenVisibility_SetsHidden()
    {
        var categoryId = await CreateCategoryAsync("Stealth", "stealth");
        var productId = await CreateProductAsync(categoryId, "Hidden Product", visibility: ProductVisibility.Hidden);

        var get = await Client.GetAsync($"catalog/products/{productId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<ProductResponse>>()).Data!;
        Assert.Equal(ProductVisibility.Hidden, data.Visibility);
    }

    [Fact]
    public async Task Create_WithNonExistentCategory_ReturnsNotFound()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            "catalog/products",
            new { name = "Orphaned", slug = "orphaned", description = (string?)null, categoryId = Guid.NewGuid(), visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task Create_WithDeletedCategory_ReturnsConflict()
    {
        var categoryId = await CreateCategoryAsync("Closing", "closing");
        var softDelete = await Client.PostAsync($"catalog/categories/{categoryId}/soft-delete", null);
        softDelete.EnsureSuccessStatusCode();

        var response = await Client.PostAsJsonAsync(
            "catalog/products",
            new { name = "Too Late", slug = "too-late", description = (string?)null, categoryId, visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The category is deleted and cannot be used.", problem.Detail);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await AuthorizeAsync();
        var categoryId = await CreateCategoryAsync("Duplicates", "duplicates");
        await CreateProductAsync(categoryId, "Same Name");

        var response = await Client.PostAsJsonAsync(
            "catalog/products",
            new { name = "Same Name", slug = "other-product-slug", description = (string?)null, categoryId, visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A product with this name already exists.", problem.Detail);
    }

    [Fact]
    public async Task Create_DuplicateSlug_ReturnsConflict()
    {
        await AuthorizeAsync();
        var categoryId = await CreateCategoryAsync("More Duplicates", "more-duplicates");
        await CreateProductAsync(categoryId, "Product One", "shared-product-slug");

        var response = await Client.PostAsJsonAsync(
            "catalog/products",
            new { name = "Product Two", slug = "shared-product-slug", description = (string?)null, categoryId, visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A product with this slug already exists.", problem.Detail);
    }

    [Fact]
    public async Task Create_EmptyName_ReturnsBadRequest()
    {
        await AuthorizeAsync();
        var categoryId = await CreateCategoryAsync("Validators", "validators");

        var response = await Client.PostAsJsonAsync(
            "catalog/products",
            new { name = string.Empty, slug = "empty-name", description = (string?)null, categoryId, visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }

    [Fact]
    public async Task Create_InvalidSlug_ReturnsBadRequest()
    {
        await AuthorizeAsync();
        var categoryId = await CreateCategoryAsync("More Validators", "more-validators");

        var response = await Client.PostAsJsonAsync(
            "catalog/products",
            new { name = "Bad Slug Product", slug = "bad slug!", description = (string?)null, categoryId, visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The slug is invalid.", problem.Detail);
    }

    [Fact]
    public async Task Update_UpdatesFields()
    {
        var categoryId = await CreateCategoryAsync("Update Targets", "update-targets");
        var productId = await CreateProductAsync(categoryId, "Before Update");

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}",
            new { name = "After Update", slug = "after-update", description = "Changed", categoryId, visibility = ProductVisibility.Hidden, seo = (object?)null });
        response.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var get = await Client.GetAsync($"catalog/products/{productId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<ProductResponse>>()).Data!;
        Assert.Equal("After Update", data.Name);
        Assert.Equal("after-update", data.Slug);
        Assert.Equal("Changed", data.Description);
        Assert.Equal(ProductVisibility.Hidden, data.Visibility);
    }

    [Fact]
    public async Task Update_NonExistentProduct_ReturnsNotFound()
    {
        await AuthorizeAsync();
        var categoryId = await CreateCategoryAsync("No Targets", "no-targets");

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{Guid.NewGuid()}",
            new { name = "Nope", slug = "nope", description = (string?)null, categoryId, visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task Activate_SetsStatusActive()
    {
        var categoryId = await CreateCategoryAsync("Lifecycle", "lifecycle");
        var productId = await CreateProductAsync(categoryId, "Activate Me");

        var response = await Client.PostAsync($"catalog/products/{productId}/activate", null);
        response.EnsureSuccessStatusCode();

        var data = await GetProductAsync(productId);
        Assert.Equal(ProductStatus.Active, data.Status);
    }

    [Fact]
    public async Task Deactivate_FromActive_SetsInactive()
    {
        var categoryId = await CreateCategoryAsync("More Lifecycle", "more-lifecycle");
        var productId = await CreateProductAsync(categoryId, "Deactivate Me");
        await PostEnsureSuccessAsync($"catalog/products/{productId}/activate");

        var response = await Client.PostAsync($"catalog/products/{productId}/deactivate", null);
        response.EnsureSuccessStatusCode();

        var data = await GetProductAsync(productId);
        Assert.Equal(ProductStatus.Inactive, data.Status);
    }

    [Fact]
    public async Task Deactivate_FromDraft_KeepsDraft()
    {
        var categoryId = await CreateCategoryAsync("Draft Lifecycle", "draft-lifecycle");
        var productId = await CreateProductAsync(categoryId, "Still Draft");

        var response = await Client.PostAsync($"catalog/products/{productId}/deactivate", null);
        response.EnsureSuccessStatusCode();

        var data = await GetProductAsync(productId);
        Assert.Equal(ProductStatus.Draft, data.Status);
    }

    [Fact]
    public async Task Archive_SetsArchived()
    {
        var categoryId = await CreateCategoryAsync("Archive Targets", "archive-targets");
        var productId = await CreateProductAsync(categoryId, "Archive Me");

        var response = await Client.PostAsync($"catalog/products/{productId}/archive", null);
        response.EnsureSuccessStatusCode();

        var data = await GetProductAsync(productId);
        Assert.Equal(ProductStatus.Archived, data.Status);
    }

    [Fact]
    public async Task Activate_ArchivedProduct_ReturnsConflict()
    {
        var categoryId = await CreateCategoryAsync("Archived Lifecycle", "archived-lifecycle");
        var productId = await CreateProductAsync(categoryId, "Archived Then Activate");
        await PostEnsureSuccessAsync($"catalog/products/{productId}/archive");

        var response = await Client.PostAsync($"catalog/products/{productId}/activate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested product status transition is not allowed from the current status.", problem.Detail);
    }

    [Fact]
    public async Task SoftDelete_MarksDeleted()
    {
        var categoryId = await CreateCategoryAsync("Soft Delete Targets", "soft-delete-targets");
        var productId = await CreateProductAsync(categoryId, "Soft Delete Me");

        var response = await Client.PostAsync($"catalog/products/{productId}/soft-delete", null);
        response.EnsureSuccessStatusCode();

        var data = await GetProductAsync(productId);
        Assert.True(data.IsDeleted);
        Assert.NotNull(data.DeletedAtUtc);
    }

    [Fact]
    public async Task Restore_UnmarksDeleted()
    {
        var categoryId = await CreateCategoryAsync("Restore Targets", "restore-targets");
        var productId = await CreateProductAsync(categoryId, "Restore Me");
        await PostEnsureSuccessAsync($"catalog/products/{productId}/soft-delete");

        var response = await Client.PostAsync($"catalog/products/{productId}/restore", null);
        response.EnsureSuccessStatusCode();

        var data = await GetProductAsync(productId);
        Assert.False(data.IsDeleted);
        Assert.Null(data.DeletedAtUtc);
    }

    [Fact]
    public async Task Update_DeletedProduct_ReturnsConflict()
    {
        var categoryId = await CreateCategoryAsync("Deleted Update Targets", "deleted-update-targets");
        var productId = await CreateProductAsync(categoryId, "Deleted Update Me");
        await PostEnsureSuccessAsync($"catalog/products/{productId}/soft-delete");

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}",
            new { name = "Changed", slug = "changed", description = (string?)null, categoryId, visibility = ProductVisibility.Visible, seo = (object?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A deleted product cannot be modified.", problem.Detail);
    }

    [Fact]
    public async Task Get_NonExistentProduct_ReturnsNotFound()
    {
        var response = await Client.GetAsync($"catalog/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task List_ReturnsOnlyNonDeletedProducts()
    {
        var categoryId = await CreateCategoryAsync("List Targets", "list-targets");
        var keepId = await CreateProductAsync(categoryId, "Keep Product");
        var deleteId = await CreateProductAsync(categoryId, "Delete Product");
        await PostEnsureSuccessAsync($"catalog/products/{deleteId}/soft-delete");

        var response = await Client.GetAsync("catalog/products");
        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<ProductResponse>>>()).Data!;
        Assert.Contains(data, product => product.Id == keepId);
        Assert.DoesNotContain(data, product => product.Id == deleteId);
    }

    private async Task<ProductResponse> GetProductAsync(Guid productId)
    {
        var get = await Client.GetAsync($"catalog/products/{productId}");
        get.EnsureSuccessStatusCode();

        return (await get.ReadJsonAsync<ApiResponseDto<ProductResponse>>()).Data!;
    }

    private async Task PostEnsureSuccessAsync(string uri)
    {
        var response = await Client.PostAsync(uri, null);
        response.EnsureSuccessStatusCode();
    }
}
