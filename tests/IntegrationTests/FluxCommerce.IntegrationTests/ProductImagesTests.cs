using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class ProductImagesTests : CatalogApiTestBase
{
    public ProductImagesTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Add_ReturnsNoContent()
    {
        var productId = await CreateProductForImagesAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/images",
            new { imageUrl = "https://cdn.test/one.png", altText = (string?)null, isPrimary = true, displayOrder = 0, metadata = (object?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Add_EmptyUrl_ReturnsBadRequest()
    {
        var productId = await CreateProductForImagesAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/images",
            new { imageUrl = string.Empty, altText = (string?)null, isPrimary = false, displayOrder = 0, metadata = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("One or more fields are invalid.", problem.Detail);
    }

    [Fact]
    public async Task Add_NonExistentProduct_ReturnsNotFound()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{Guid.NewGuid()}/images",
            new { imageUrl = "https://cdn.test/orphan.png", altText = (string?)null, isPrimary = false, displayOrder = 0, metadata = (object?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task Add_SecondPrimary_ClearsFirstPrimary()
    {
        var productId = await CreateProductForImagesAsync();
        var firstId = await AddImageAsync(productId, "https://cdn.test/first.png", isPrimary: true, displayOrder: 0);
        var secondId = await AddImageAsync(productId, "https://cdn.test/second.png", isPrimary: true, displayOrder: 1);

        var list = await GetImagesAsync(productId);

        Assert.True(list.Single(image => image.Id == secondId).IsPrimary);
        Assert.False(list.Single(image => image.Id == firstId).IsPrimary);
    }

    [Fact]
    public async Task Update_UpdatesUrlAltTextAndOrder()
    {
        var productId = await CreateProductForImagesAsync();
        var imageId = await AddImageAsync(productId, "https://cdn.test/old.png", isPrimary: false, displayOrder: 0);

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}/images/{imageId}",
            new { imageUrl = "https://cdn.test/new.png", altText = "New alt", displayOrder = 5, metadata = (object?)null });
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/products/{productId}/images/{imageId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<ProductImageResponse>>()).Data!;
        Assert.Equal("https://cdn.test/new.png", data.ImageUrl);
        Assert.Equal("New alt", data.AltText);
        Assert.Equal(5, data.DisplayOrder);
    }

    [Fact]
    public async Task Delete_RemovesImage()
    {
        var productId = await CreateProductForImagesAsync();
        var imageId = await AddImageAsync(productId, "https://cdn.test/gone.png", isPrimary: false, displayOrder: 0);

        var response = await Client.DeleteAsync($"catalog/products/{productId}/images/{imageId}");
        response.EnsureSuccessStatusCode();

        var list = await GetImagesAsync(productId);
        Assert.DoesNotContain(list, image => image.Id == imageId);
    }

    [Fact]
    public async Task SetPrimary_SwitchesPrimary()
    {
        var productId = await CreateProductForImagesAsync();
        var firstId = await AddImageAsync(productId, "https://cdn.test/a.png", isPrimary: true, displayOrder: 0);
        var secondId = await AddImageAsync(productId, "https://cdn.test/b.png", isPrimary: false, displayOrder: 1);

        var response = await Client.PostAsync($"catalog/products/{productId}/images/{secondId}/set-primary", null);
        response.EnsureSuccessStatusCode();

        var list = await GetImagesAsync(productId);
        Assert.True(list.Single(image => image.Id == secondId).IsPrimary);
        Assert.False(list.Single(image => image.Id == firstId).IsPrimary);
    }

    [Fact]
    public async Task Reorder_AppliesNewOrder()
    {
        var productId = await CreateProductForImagesAsync();
        var firstId = await AddImageAsync(productId, "https://cdn.test/1.png", isPrimary: false, displayOrder: 0);
        var secondId = await AddImageAsync(productId, "https://cdn.test/2.png", isPrimary: false, displayOrder: 1);
        var thirdId = await AddImageAsync(productId, "https://cdn.test/3.png", isPrimary: false, displayOrder: 2);

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}/images/reorder",
            new { orderedImageIds = new[] { thirdId, secondId, firstId } });
        response.EnsureSuccessStatusCode();

        var list = await GetImagesAsync(productId);
        Assert.Equal(0, list.Single(image => image.Id == thirdId).DisplayOrder);
        Assert.Equal(1, list.Single(image => image.Id == secondId).DisplayOrder);
        Assert.Equal(2, list.Single(image => image.Id == firstId).DisplayOrder);
    }

    [Fact]
    public async Task Reorder_WithUnknownImage_ReturnsConflict()
    {
        var productId = await CreateProductForImagesAsync();
        var firstId = await AddImageAsync(productId, "https://cdn.test/1.png", isPrimary: false, displayOrder: 0);

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}/images/reorder",
            new { orderedImageIds = new[] { firstId, Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested image order is invalid.", problem.Detail);
    }

    [Fact]
    public async Task Get_NonExistentImage_ReturnsNotFound()
    {
        var productId = await CreateProductForImagesAsync();

        var response = await Client.GetAsync($"catalog/products/{productId}/images/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The product does not have the specified image.", problem.Detail);
    }

    [Fact]
    public async Task List_ReturnsAllImages()
    {
        var productId = await CreateProductForImagesAsync();
        await AddImageAsync(productId, "https://cdn.test/x.png", isPrimary: true, displayOrder: 0);
        await AddImageAsync(productId, "https://cdn.test/y.png", isPrimary: false, displayOrder: 1);

        var list = await GetImagesAsync(productId);

        Assert.Equal(2, list.Count);
    }

    private async Task<Guid> CreateProductForImagesAsync()
    {
        var categoryId = await CreateCategoryAsync("Image Products", "image-products");
        return await CreateProductAsync(categoryId, "Imaged Product");
    }

    private async Task<Guid> AddImageAsync(Guid productId, string imageUrl, bool isPrimary, int displayOrder)
    {
        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/images",
            new { imageUrl, altText = (string?)null, isPrimary, displayOrder, metadata = (object?)null });
        response.EnsureSuccessStatusCode();

        return (await GetImagesAsync(productId))
            .Single(image => image.ImageUrl == imageUrl)
            .Id;
    }

    private async Task<IReadOnlyCollection<ProductImageResponse>> GetImagesAsync(Guid productId)
    {
        var response = await Client.GetAsync($"catalog/products/{productId}/images");
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<ProductImageResponse>>>()).Data!;
    }
}
