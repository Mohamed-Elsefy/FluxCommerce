using FluxCommerce.Modules.Catalog.Application.Services;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CatalogVariantCheckoutLookupTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 7, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetCheckoutInfoAsync_RequestsVariantsThroughSingleBatchCall_NotPerVariant()
    {
        var variantIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var variantRepository = new Mock<IVariantRepository>();
        variantRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Variant>());
        var productRepository = new Mock<IProductRepository>();
        productRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Product>());
        var lookup = new CatalogVariantCheckoutLookup(variantRepository.Object, productRepository.Object);

        await lookup.GetCheckoutInfoAsync(variantIds, CancellationToken.None);

        variantRepository.Verify(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        variantRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_RequestsProductsThroughSingleBatchCall()
    {
        var product = CreateActiveProduct();
        var variant = CreateActiveVariant(product.Id);
        var variantRepository = new Mock<IVariantRepository>();
        variantRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Contains(variant.Id) ? new[] { variant } : Array.Empty<Variant>());
        var productRepository = new Mock<IProductRepository>();
        productRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Contains(product.Id) ? new[] { product } : Array.Empty<Product>());
        var lookup = new CatalogVariantCheckoutLookup(variantRepository.Object, productRepository.Object);

        await lookup.GetCheckoutInfoAsync(new[] { variant.Id }, CancellationToken.None);

        productRepository.Verify(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        productRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_ActiveVariantUnderActiveVisibleProduct_IsAvailable_WithCatalogSnapshot()
    {
        var product = CreateActiveProduct();
        var variant = CreateActiveVariant(product.Id);
        var lookup = CreateLookup(variant, product);

        var results = await lookup.GetCheckoutInfoAsync(new[] { variant.Id }, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CatalogVariantEligibilityStatus.Available, result.Status);
        Assert.Equal(product.Id, result.ProductId);
        Assert.Equal(variant.Sku.Value, result.Sku);
        Assert.Equal(product.Name, result.ProductName);
        Assert.Equal(variant.UnitPrice.Amount, result.UnitPriceAmount);
        Assert.Equal(variant.UnitPrice.Currency, result.Currency);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_UnknownVariant_IsMissing()
    {
        var lookup = CreateLookup(Array.Empty<Variant>(), Array.Empty<Product>());
        var missingId = Guid.NewGuid();

        var results = await lookup.GetCheckoutInfoAsync(new[] { missingId }, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CatalogVariantEligibilityStatus.Missing, result.Status);
        Assert.Null(result.ProductId);
        Assert.Null(result.Sku);
        Assert.Null(result.ProductName);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_InactiveVariant_IsUnavailable()
    {
        var product = CreateActiveProduct();
        var variant = CreateActiveVariant(product.Id);
        variant.Deactivate();

        var results = await CreateLookup(variant, product).GetCheckoutInfoAsync(new[] { variant.Id }, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CatalogVariantEligibilityStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_VariantUnderDraftProduct_IsUnavailable()
    {
        var product = CreateActiveProduct();
        var variant = CreateActiveVariant(product.Id);
        product.Deactivate();

        var results = await CreateLookup(variant, product).GetCheckoutInfoAsync(new[] { variant.Id }, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(CatalogVariantEligibilityStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_EmptyRequest_ReturnsEmptyWithoutQuerying()
    {
        var variantRepository = new Mock<IVariantRepository>();
        var productRepository = new Mock<IProductRepository>();
        var lookup = new CatalogVariantCheckoutLookup(variantRepository.Object, productRepository.Object);

        var results = await lookup.GetCheckoutInfoAsync(Array.Empty<Guid>(), CancellationToken.None);

        Assert.Empty(results);
        variantRepository.Verify(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        productRepository.Verify(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Product CreateActiveProduct()
    {
        var product = Product.Create(
            Guid.NewGuid(),
            "Wireless Mouse",
            new Slug("wireless-mouse"),
            Guid.NewGuid(),
            UtcNow);
        product.Activate();
        return product;
    }

    private static Variant CreateActiveVariant(Guid productId) =>
        Variant.Create(
            Guid.NewGuid(),
            productId,
            new Sku("SKU-1001"),
            "Wireless Mouse",
            new Money(49.99m, "USD"),
            UtcNow);

    private static CatalogVariantCheckoutLookup CreateLookup(Variant variant, Product product) =>
        CreateLookup(new[] { variant }, new[] { product });

    private static CatalogVariantCheckoutLookup CreateLookup(
        IEnumerable<Variant> variants,
        IEnumerable<Product> products)
    {
        var variantsById = variants.ToDictionary(variant => variant.Id);
        var productsById = products.ToDictionary(product => product.Id);
        var variantRepository = new Mock<IVariantRepository>();
        variantRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Select(id => variantsById.GetValueOrDefault(id)).Where(variant => variant is not null).Cast<Variant>().ToArray());
        var productRepository = new Mock<IProductRepository>();
        productRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Select(id => productsById.GetValueOrDefault(id)).Where(product => product is not null).Cast<Product>().ToArray());

        return new CatalogVariantCheckoutLookup(variantRepository.Object, productRepository.Object);
    }
}