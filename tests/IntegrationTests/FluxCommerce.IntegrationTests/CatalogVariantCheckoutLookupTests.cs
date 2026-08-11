using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Domain.ValueObjects;
using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Integration coverage for the real <see cref="ICatalogVariantCheckoutLookup"/> implementation that
/// Orders will rely on for batch variant revalidation at Checkout (Phase 4.5). The lookup is resolved
/// from the hosted Catalog DI container and exercised against the in-memory Catalog database fixture.
/// </summary>
[Collection(CatalogTestCollection.Name)]
public class CatalogVariantCheckoutLookupTests : CatalogApiTestBase
{
    public CatalogVariantCheckoutLookupTests(CatalogWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_MarksActiveVisibleVariantAvailable_AndUnknownVariantMissing()
    {
        var variantId = Guid.NewGuid();
        await SeedAsync(activeVariantId: variantId);
        var missingId = Guid.NewGuid();

        var results = await LookupAsync([variantId, missingId]);

        Assert.Contains(results, result => result.VariantId == variantId && result.Status == CatalogVariantEligibilityStatus.Available);
        Assert.Contains(results, result => result.VariantId == missingId && result.Status == CatalogVariantEligibilityStatus.Missing);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_ReturnsCurrentCatalogIdentityForAvailableVariant()
    {
        var variantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await SeedAsync(activeVariantId: variantId, productId: productId);

        var results = await LookupAsync([variantId]);

        var result = Assert.Single(results, item => item.VariantId == variantId);
        Assert.Equal(CatalogVariantEligibilityStatus.Available, result.Status);
        Assert.Equal(productId, result.ProductId);
        Assert.NotNull(result.Sku);
        Assert.Equal("Lookup Product", result.ProductName);
        Assert.Equal(89.99m, result.UnitPriceAmount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_MarksInactiveVariantUnavailable()
    {
        var variantId = Guid.NewGuid();
        await SeedAsync(activeVariantId: null, inactiveVariantId: variantId);

        var results = await LookupAsync([variantId]);

        Assert.Contains(results, result => result.VariantId == variantId && result.Status == CatalogVariantEligibilityStatus.Unavailable);
    }

    [Fact]
    public async Task GetCheckoutInfoAsync_MarksVariantUnderInactiveProductUnavailable()
    {
        var variantId = Guid.NewGuid();
        await SeedAsync(activeVariantId: variantId, productActive: false);

        var results = await LookupAsync([variantId]);

        Assert.Contains(results, result => result.VariantId == variantId && result.Status == CatalogVariantEligibilityStatus.Unavailable);
    }

    private async Task<IReadOnlyCollection<CatalogVariantCheckoutInfo>> LookupAsync(IReadOnlyCollection<Guid> variantIds)
    {
        using var scope = Factory.Services.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<ICatalogVariantCheckoutLookup>();

        return await lookup.GetCheckoutInfoAsync(variantIds, CancellationToken.None);
    }

    private async Task SeedAsync(
        Guid? activeVariantId = null,
        Guid? inactiveVariantId = null,
        Guid? productId = null,
        bool productActive = true)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var utcNow = DateTime.UtcNow;
        var category = Category.Create(
            Guid.NewGuid(),
            "Lookup Category",
            new Slug($"lookup-cat-{Guid.NewGuid():N}"),
            utcNow);

        var product = Product.Create(
            productId ?? Guid.NewGuid(),
            "Lookup Product",
            new Slug($"lookup-prod-{Guid.NewGuid():N}"),
            category.Id,
            utcNow);

        if (productActive)
            product.Activate();

        dbContext.Add(category);
        dbContext.Add(product);

        if (activeVariantId is Guid activeId)
        {
            dbContext.Add(Variant.Create(
                activeId,
                product.Id,
                new Sku($"SKU-ACT-{Guid.NewGuid():N}"),
                "Active Variant",
                new Money(89.99m, "USD"),
                utcNow));
        }

        if (inactiveVariantId is Guid inactiveId)
        {
            var inactiveVariant = Variant.Create(
                inactiveId,
                product.Id,
                new Sku($"SKU-INA-{Guid.NewGuid():N}"),
                "Inactive Variant",
                new Money(79.99m, "USD"),
                utcNow);
            inactiveVariant.Deactivate();
            dbContext.Add(inactiveVariant);
        }

        await dbContext.SaveChangesAsync();
    }
}