using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartHydrateTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddHours(3);

    private static CartItem CreateItem(string sku = "SKU-A", int quantity = 2) =>
        new(
            ProductId.Create(),
            VariantId.Create(),
            new Sku(sku),
            "Product A",
            new Money(19.99m, "USD"),
            quantity);

    [Fact]
    public void Hydrate_WithFullState_RestoresAggregate()
    {
        var id = CartId.Create();
        var items = new[] { CreateItem(), CreateItem(sku: "SKU-B", quantity: 5) };

        var cart = Cart.Hydrate(id, CartOwner.Guest(), version: 7, CreatedAt, UpdatedAt, items);

        Assert.Equal(id, cart.Id);
        Assert.True(cart.IsGuest);
        Assert.Null(cart.UserId);
        Assert.Equal(7, cart.Version);
        Assert.Equal(CreatedAt, cart.CreatedAtUtc);
        Assert.Equal(UpdatedAt, cart.UpdatedAtUtc);
        Assert.Equal(2, cart.Items.Count);
    }

    [Fact]
    public void Hydrate_Authenticated_RestoresOwner()
    {
        var userId = Guid.NewGuid();
        var cart = Cart.Hydrate(CartId.Create(), CartOwner.Authenticated(userId), 3, CreatedAt, UpdatedAt, []);

        Assert.True(cart.IsAuthenticated);
        Assert.Equal(userId, cart.UserId);
    }

    [Fact]
    public void Hydrate_WithEmptyItems_RestoresEmptyCart()
    {
        var cart = Cart.Hydrate(CartId.Create(), CartOwner.Guest(), 2, CreatedAt, UpdatedAt, []);

        Assert.Empty(cart.Items);
    }

    [Fact]
    public void Hydrate_WithNullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Cart.Hydrate(null!, CartOwner.Guest(), 1, CreatedAt, UpdatedAt, []));
    }

    [Fact]
    public void Hydrate_WithNullOwner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Cart.Hydrate(CartId.Create(), null!, 1, CreatedAt, UpdatedAt, []));
    }

    [Fact]
    public void Hydrate_WithVersionBelowInitial_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Cart.Hydrate(CartId.Create(), CartOwner.Guest(), 0, CreatedAt, UpdatedAt, []));
    }

    [Fact]
    public void Hydrate_WithDefaultCreatedAt_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Cart.Hydrate(CartId.Create(), CartOwner.Guest(), 1, default, UpdatedAt, []));
    }

    [Fact]
    public void Hydrate_WithDuplicateVariants_Throws()
    {
        var variantId = VariantId.Create();
        var items = new[]
        {
            new CartItem(ProductId.Create(), variantId, new Sku("SKU-A"), "Product A", new Money(9.99m, "USD"), 1),
            new CartItem(ProductId.Create(), variantId, new Sku("SKU-B"), "Product B", new Money(4.99m, "USD"), 2),
        };

        Assert.Throws<ArgumentException>(() =>
            Cart.Hydrate(CartId.Create(), CartOwner.Guest(), 1, CreatedAt, UpdatedAt, items));
    }

    [Fact]
    public void Hydrate_RestoredAggregate_MutatesNormally()
    {
        var variantId = VariantId.Create();
        var items = new[]
        {
            new CartItem(ProductId.Create(), variantId, new Sku("SKU-A"), "Product A", new Money(9.99m, "USD"), 1),
        };

        var cart = Cart.Hydrate(CartId.Create(), CartOwner.Guest(), version: 5, CreatedAt, UpdatedAt, items);
        var later = UpdatedAt.AddMinutes(1);

        var result = cart.AddItem(
            ProductId.Create(),
            VariantId.Create(),
            new Sku("SKU-C"),
            "Product C",
            new Money(3.99m, "USD"),
            4,
            later);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, cart.Version);
        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(later, cart.UpdatedAtUtc);
    }
}
