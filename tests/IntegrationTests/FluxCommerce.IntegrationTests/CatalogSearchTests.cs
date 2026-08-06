using System.Net;
using System.Net.Http;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class CatalogSearchTests : CatalogApiTestBase
{
    public CatalogSearchTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Search_WithNoCriteria_ReturnsEmptyPage()
    {
        var response = await Client.GetAsync("/catalog");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Empty(data.Items);
        Assert.Equal(0, data.TotalCount);
        Assert.Equal(1, data.PageNumber);
        Assert.Equal(20, data.PageSize);
    }

    [Fact]
    public async Task Search_ReturnsAllNonDeletedProductsInCategory()
    {
        var categoryId = await CreateCategoryAsync("Search Category", "search-category");
        await CreateProductAsync(categoryId, "Alpha Product");
        await CreateProductAsync(categoryId, "Beta Product");

        var response = await Client.GetAsync($"/catalog?categoryId={categoryId}");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Equal(2, data.TotalCount);
        Assert.Equal(2, data.Items.Count);
    }

    [Fact]
    public async Task Search_ExcludesSoftDeletedProducts()
    {
        var categoryId = await CreateCategoryAsync("Search Deleted", "search-deleted");
        var keepId = await CreateProductAsync(categoryId, "Keep Product");
        var deleteId = await CreateProductAsync(categoryId, "Delete Product");
        var softDelete = await Client.PostAsync($"catalog/products/{deleteId}/soft-delete", null);
        softDelete.EnsureSuccessStatusCode();

        var response = await Client.GetAsync($"/catalog?categoryId={categoryId}");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Equal(1, data.TotalCount);
        Assert.Equal(keepId, data.Items.Single().Id);
    }

    [Fact]
    public async Task Search_FiltersBySearchTerm()
    {
        var categoryId = await CreateCategoryAsync("Search Terms", "search-terms");
        await CreateProductAsync(categoryId, "Red Widget");
        await CreateProductAsync(categoryId, "Blue Gadget");

        var response = await Client.GetAsync($"/catalog?searchTerm=widget");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        var item = Assert.Single(data.Items);
        Assert.Equal("Red Widget", item.Name);
    }

    [Fact]
    public async Task Search_FiltersByCategoryId()
    {
        var firstCategoryId = await CreateCategoryAsync("First Search Cat", "first-search-cat");
        var secondCategoryId = await CreateCategoryAsync("Second Search Cat", "second-search-cat");
        var firstProductId = await CreateProductAsync(firstCategoryId, "First Product");
        await CreateProductAsync(secondCategoryId, "Second Product");

        var response = await Client.GetAsync($"/catalog?categoryId={firstCategoryId}");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Equal(1, data.TotalCount);
        Assert.Equal(firstProductId, data.Items.Single().Id);
    }

    [Fact]
    public async Task Search_FiltersByVisibility()
    {
        var categoryId = await CreateCategoryAsync("Visibility Search", "visibility-search");
        await CreateProductAsync(categoryId, "Visible Product");
        await CreateProductAsync(categoryId, "Hidden Product", visibility: ProductVisibility.Hidden);

        var response = await Client.GetAsync($"/catalog?visibility=Hidden");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Equal(1, data.TotalCount);
        Assert.Equal("Hidden Product", data.Items.Single().Name);
    }

    [Fact]
    public async Task Search_FiltersByStatus()
    {
        var categoryId = await CreateCategoryAsync("Status Search", "status-search");
        await CreateProductAsync(categoryId, "Draft Product");
        var activeId = await CreateProductAsync(categoryId, "Active Product");
        var activate = await Client.PostAsync($"catalog/products/{activeId}/activate", null);
        activate.EnsureSuccessStatusCode();

        var response = await Client.GetAsync($"/catalog?status=Active");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Equal(1, data.TotalCount);
        Assert.Equal(activeId, data.Items.Single().Id);
    }

    [Fact]
    public async Task Search_Paginates()
    {
        var categoryId = await CreateCategoryAsync("Pagination", "pagination");
        await CreateProductAsync(categoryId, "Page Product One");
        await CreateProductAsync(categoryId, "Page Product Two");
        await CreateProductAsync(categoryId, "Page Product Three");

        var first = await Client.GetAsync($"/catalog?categoryId={categoryId}&pageSize=2&pageNumber=1");
        first.EnsureSuccessStatusCode();
        var firstData = (await first.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Equal(3, firstData.TotalCount);
        Assert.Equal(2, firstData.Items.Count);
        Assert.Equal(1, firstData.PageNumber);

        var second = await Client.GetAsync($"/catalog?categoryId={categoryId}&pageSize=2&pageNumber=2");
        second.EnsureSuccessStatusCode();
        var secondData = (await second.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Single(secondData.Items);
        Assert.Equal(2, secondData.PageNumber);
    }

    [Fact]
    public async Task Search_SortsByNameDescending()
    {
        var categoryId = await CreateCategoryAsync("Sorting", "sorting");
        await CreateProductAsync(categoryId, "Alpha Product");
        await CreateProductAsync(categoryId, "Beta Product");
        await CreateProductAsync(categoryId, "Gamma Product");

        var response = await Client.GetAsync($"/catalog?categoryId={categoryId}&sortBy=Name&sortDirection=Descending");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Equal("Gamma Product", data.Items.First().Name);
        Assert.Equal("Alpha Product", data.Items.Last().Name);
    }

    [Fact]
    public async Task Search_SortsByNameAscending()
    {
        var categoryId = await CreateCategoryAsync("Sorting Asc", "sorting-asc");
        await CreateProductAsync(categoryId, "Gamma Product");
        await CreateProductAsync(categoryId, "Alpha Product");

        var response = await Client.GetAsync($"/catalog?categoryId={categoryId}&sortBy=Name&sortDirection=Ascending");

        response.EnsureSuccessStatusCode();

        var data = (await response.ReadJsonAsync<ApiResponseDto<PagedResult<CatalogProductListItem>>>()).Data!;
        Assert.Equal("Alpha Product", data.Items.First().Name);
    }

    [Fact]
    public async Task Search_InvalidPageNumber_ReturnsBadRequest()
    {
        var response = await Client.GetAsync("/catalog?pageNumber=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }

    [Fact]
    public async Task Search_InvalidPageSize_ReturnsBadRequest()
    {
        var response = await Client.GetAsync("/catalog?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }
}
