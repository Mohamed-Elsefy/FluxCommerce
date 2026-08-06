using System.Net;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(CatalogTestCollection.Name)]
public class ProductVariantsTests : CatalogApiTestBase
{
    public ProductVariantsTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithId()
    {
        var productId = await CreateProductForVariantsAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/variants",
            new { sku = "SKU-001", name = "Small" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
        Assert.NotEqual(Guid.Empty, data);
    }

    [Fact]
    public async Task Create_DuplicateSku_ReturnsConflict()
    {
        var productId = await CreateProductForVariantsAsync();
        await PostVariantAsync(productId, "SKU-DUP", "Small");

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/variants",
            new { sku = "SKU-DUP", name = "Large" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A variant with this SKU already exists.", problem.Detail);
    }

    [Fact]
    public async Task Create_InvalidSku_ReturnsBadRequest()
    {
        var productId = await CreateProductForVariantsAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/variants",
            new { sku = "bad sku!", name = "Broken" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The SKU is invalid.", problem.Detail);
    }

    [Fact]
    public async Task Create_NonExistentProduct_ReturnsNotFound()
    {
        await AuthorizeAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{Guid.NewGuid()}/variants",
            new { sku = "SKU-999", name = "Orphan" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The requested resource was not found.", problem.Detail);
    }

    [Fact]
    public async Task CreateBatch_CreatesAllVariants()
    {
        var productId = await CreateProductForVariantsAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/variants/batch",
            new
            {
                variants = new[]
                {
                    new { sku = "SKU-B1", name = "Red" },
                    new { sku = "SKU-B2", name = "Blue" },
                }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await response.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<Guid>>>()).Data!;
        Assert.Equal(2, data.Count);

        var list = await Client.GetAsync($"catalog/products/{productId}/variants");
        list.EnsureSuccessStatusCode();

        var variants = (await list.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<VariantResponse>>>()).Data!;
        Assert.Equal(2, variants.Count);
    }

    [Fact]
    public async Task CreateBatch_WithDuplicateSkusInBatch_ReturnsConflict()
    {
        var productId = await CreateProductForVariantsAsync();

        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/variants/batch",
            new
            {
                variants = new[]
                {
                    new { sku = "SKU-SAME", name = "Red" },
                    new { sku = "SKU-SAME", name = "Blue" },
                }
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("A variant with this SKU already exists.", problem.Detail);
    }

    [Fact]
    public async Task Update_UpdatesSkuAndName()
    {
        var productId = await CreateProductForVariantsAsync();
        var variantId = await PostVariantAsync(productId, "SKU-OLD", "Old Name");

        var response = await Client.PutAsJsonAsync(
            $"catalog/products/{productId}/variants/{variantId}",
            new { sku = "SKU-NEW", name = "New Name" });
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/products/{productId}/variants/{variantId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<VariantResponse>>()).Data!;
        Assert.Equal("SKU-NEW", data.Sku);
        Assert.Equal("New Name", data.Name);
    }

    [Fact]
    public async Task Delete_SoftDeletesVariant()
    {
        var productId = await CreateProductForVariantsAsync();
        var variantId = await PostVariantAsync(productId, "SKU-DEL", "Gone");

        var response = await Client.DeleteAsync($"catalog/products/{productId}/variants/{variantId}");
        response.EnsureSuccessStatusCode();

        var get = await Client.GetAsync($"catalog/products/{productId}/variants/{variantId}");
        get.EnsureSuccessStatusCode();

        var data = (await get.ReadJsonAsync<ApiResponseDto<VariantResponse>>()).Data!;
        Assert.True(data.IsDeleted);
        Assert.NotNull(data.DeletedAtUtc);
    }

    [Fact]
    public async Task Delete_ExcludesFromList()
    {
        var productId = await CreateProductForVariantsAsync();
        var variantId = await PostVariantAsync(productId, "SKU-KEEP-DEL", "To Delete");
        await PostVariantAsync(productId, "SKU-KEEP", "To Keep");
        var delete = await Client.DeleteAsync($"catalog/products/{productId}/variants/{variantId}");
        delete.EnsureSuccessStatusCode();

        var list = await Client.GetAsync($"catalog/products/{productId}/variants");
        list.EnsureSuccessStatusCode();

        var variants = (await list.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<VariantResponse>>>()).Data!;
        Assert.DoesNotContain(variants, variant => variant.Id == variantId);
    }

    [Fact]
    public async Task Get_NonExistentVariant_ReturnsNotFound()
    {
        var productId = await CreateProductForVariantsAsync();

        var response = await Client.GetAsync($"catalog/products/{productId}/variants/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync<ProblemDetailsDto>();
        Assert.Equal("The variant was not found.", problem.Detail);
    }

    [Fact]
    public async Task List_ForProduct_ReturnsOnlyThatProductVariants()
    {
        var categoryId = await CreateCategoryAsync("Variant Isolation", "variant-isolation");
        var firstProductId = await CreateProductAsync(categoryId, "First Variant Product");
        var secondProductId = await CreateProductAsync(categoryId, "Second Variant Product");

        var firstVariantId = await PostVariantAsync(firstProductId, "SKU-FIRST", "First");
        await PostVariantAsync(secondProductId, "SKU-SECOND", "Second");

        var list = await Client.GetAsync($"catalog/products/{firstProductId}/variants");
        list.EnsureSuccessStatusCode();

        var variants = (await list.ReadJsonAsync<ApiResponseDto<IReadOnlyCollection<VariantResponse>>>()).Data!;
        Assert.Contains(variants, variant => variant.Id == firstVariantId);
        Assert.All(variants, variant => Assert.Equal(firstProductId, variant.ProductId));
    }

    private async Task<Guid> CreateProductForVariantsAsync()
    {
        var categoryId = await CreateCategoryAsync("Variant Products", "variant-products");
        return await CreateProductAsync(categoryId, "Varianted Product");
    }

    private async Task<Guid> PostVariantAsync(Guid productId, string sku, string name)
    {
        var response = await Client.PostAsJsonAsync(
            $"catalog/products/{productId}/variants",
            new { sku, name });
        response.EnsureSuccessStatusCode();

        return (await response.ReadJsonAsync<ApiResponseDto<Guid>>()).Data;
    }
}
