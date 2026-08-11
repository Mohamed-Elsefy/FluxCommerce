using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Phase 4.6 pricing and immutable snapshot coverage for
/// <see cref="CheckoutOrderSnapshotBuilder"/>. The Cart price snapshot is never authoritative and the
/// Order snapshot must use the current (revalidated) Catalog price.
/// </summary>
public class CheckoutOrderSnapshotBuilderTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    private static readonly CustomerId CustomerId = new(Guid.NewGuid());

    private static readonly OrderAddress ShippingAddress = new(
        "Ada Lovelace",
        "1 Analytical Engine Way",
        null,
        "London",
        "Greater London",
        "SW1A 1AA",
        "GB");

    private static readonly OrderAddress BillingAddress = ShippingAddress;

    private readonly CheckoutOrderSnapshotBuilder _builder = new();

    [Fact]
    public void Build_ResolvesCurrentCatalogPrice_NotCartPrice()
    {
        var cart = CreateCart(new CartCheckoutItemSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SKU-1",
            2,
            5.00m,
            "USD"));
        var revalidation = CreateRevalidation(
            cart.Items[0].VariantId,
            cart.Items[0].ProductId,
            "SKU-1",
            "Wireless Mouse",
            19.50m,
            "USD");

        var result = _builder.Build(
            cart,
            revalidation,
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsSuccess);
        var order = result.Value;
        var line = Assert.Single(order.LineItems);
        Assert.Equal(19.50m, line.UnitPrice.Amount);
        Assert.Equal("USD", line.UnitPrice.Currency);
    }

    [Fact]
    public void Build_LineTotalIsUnitPriceTimesQuantity()
    {
        var cart = CreateCart(new CartCheckoutItemSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SKU-1",
            3,
            10.00m,
            "USD"));
        var revalidation = CreateRevalidation(
            cart.Items[0].VariantId,
            cart.Items[0].ProductId,
            "SKU-1",
            "Wireless Mouse",
            19.99m,
            "USD");

        var result = _builder.Build(
            cart,
            revalidation,
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsSuccess);
        var line = Assert.Single(result.Value.LineItems);
        Assert.Equal(19.99m * 3, line.LineTotal.Amount);
    }

    [Fact]
    public void Build_SubtotalEqualsSumOfLineTotals()
    {
        var first = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", 2, 1m, "USD");
        var second = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-2", 1, 1m, "USD");
        var cart = CreateCart(first, second);

        var revalidation = new[]
        {
            ToInfo(first, "Wireless Mouse", 10.00m, "USD"),
            ToInfo(second, "Keyboard", 20.00m, "USD"),
        };

        var result = _builder.Build(
            cart,
            revalidation,
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsSuccess);
        var order = result.Value;
        Assert.Equal(2, order.LineItems.Count);
        Assert.Equal(40.00m, order.Subtotal.Amount);
    }

    [Fact]
    public void Build_GrandTotalEqualsSubtotal_WhenPhasingTotalsAreZero()
    {
        var item = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", 2, 1m, "USD");
        var cart = CreateCart(item);
        var revalidation = CreateRevalidation(item.VariantId, item.ProductId, "SKU-1", "Wireless Mouse", 25.00m, "USD");

        var result = _builder.Build(
            cart,
            revalidation,
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsSuccess);
        var order = result.Value;
        Assert.Equal(0m, order.Discount.Amount);
        Assert.Equal(0m, order.ShippingCost.Amount);
        Assert.Equal(order.Subtotal.Amount, order.GrandTotal.Amount);
    }

    [Fact]
    public void Build_WithEmptyCart_ReturnsCartEmpty()
    {
        var result = _builder.Build(
            CreateCart(),
            Array.Empty<CatalogVariantCheckoutInfo>(),
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CheckoutErrors.CartEmpty.Code, result.Error.Code);
    }

    [Fact]
    public void Build_WithUnavailableVariant_ReturnsVariantUnavailable()
    {
        var item = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", 1, 5m, "USD");
        var revalidation = new[]
        {
            new CatalogVariantCheckoutInfo(item.VariantId, CatalogVariantEligibilityStatus.Unavailable),
        };

        var result = _builder.Build(
            CreateCart(item),
            revalidation,
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CheckoutErrors.VariantUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public void Build_WithMissingVariant_ReturnsVariantUnavailable()
    {
        var item = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", 1, 5m, "USD");

        var result = _builder.Build(
            CreateCart(item),
            Array.Empty<CatalogVariantCheckoutInfo>(),
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CheckoutErrors.VariantUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public void Build_WithMixedCurrencies_ReturnsCurrencyMismatch()
    {
        var first = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", 1, 1m, "USD");
        var second = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-2", 1, 1m, "EUR");
        var cart = CreateCart(first, second);

        var revalidation = new[]
        {
            ToInfo(first, "Wireless Mouse", 10.00m, "USD"),
            ToInfo(second, "Keyboard", 20.00m, "EUR"),
        };

        var result = _builder.Build(
            cart,
            revalidation,
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CheckoutErrors.CurrencyMismatch.Code, result.Error.Code);
    }

    [Fact]
    public void Build_WithDuplicateVariant_ReturnsDuplicateVariant()
    {
        var first = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", 1, 1m, "USD");
        var duplicate = first with { Quantity = 2 };
        var cart = CreateCart(first, duplicate);

        var revalidation = new[]
        {
            ToInfo(first, "Wireless Mouse", 10.00m, "USD"),
        };

        var result = _builder.Build(
            cart,
            revalidation,
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(CheckoutErrors.DuplicateVariant.Code, result.Error.Code);
    }

    [Fact]
    public void Build_SnapshotIsImmutable_AndIndependentFromCatalog()
    {
        var item = new CartCheckoutItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", 1, 5m, "USD");
        var cart = CreateCart(item);

        var atBuildTime = CreateRevalidation(item.VariantId, item.ProductId, "SKU-1", "Wireless Mouse", 30.00m, "USD");

        var result = _builder.Build(
            cart,
            atBuildTime,
            CustomerId,
            new OrderNumber("ORD-1"),
            ShippingAddress,
            BillingAddress,
            UtcNow);

        Assert.True(result.IsSuccess);
        var order = result.Value;
        var line = Assert.Single(order.LineItems);

        // A later Catalog price change must not alter the already-built Order snapshot.
        _ = CreateRevalidation(item.VariantId, item.ProductId, "SKU-1", "Wireless Mouse", 999.00m, "USD");

        Assert.Equal(30.00m, line.UnitPrice.Amount);
        Assert.Equal("USD", line.UnitPrice.Currency);
        Assert.Equal(30.00m, order.Subtotal.Amount);
    }

    private static CartCheckoutSnapshot CreateCart(params CartCheckoutItemSnapshot[] items) =>
        new(Guid.NewGuid(), CustomerId.Value, items.ToList());

    private static CatalogVariantCheckoutInfo ToInfo(
        CartCheckoutItemSnapshot item,
        string productName,
        decimal amount,
        string currency) =>
        new(
            item.VariantId,
            CatalogVariantEligibilityStatus.Available,
            item.ProductId,
            item.Sku,
            productName,
            amount,
            currency);

    private static IReadOnlyCollection<CatalogVariantCheckoutInfo> CreateRevalidation(
        Guid variantId,
        Guid productId,
        string sku,
        string productName,
        decimal amount,
        string currency) =>
        new[]
        {
            new CatalogVariantCheckoutInfo(
                variantId,
                CatalogVariantEligibilityStatus.Available,
                productId,
                sku,
                productName,
                amount,
                currency),
        };
}