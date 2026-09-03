using FluxCommerce.Modules.Cart.Domain.Aggregates;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartMutationStateTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Version_AdvancesOnEveryStateChangingMutation()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);
        var initialVersion = cart.Version;

        var variantA = VariantId.Create();
        var variantB = VariantId.Create();

        cart.AddItem(ProductId.Create(), variantA, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);
        Assert.Equal(initialVersion + 1, cart.Version);

        cart.AddItem(ProductId.Create(), variantB, new Sku("SKU-B"), "Product B", new Money(9.99m, "USD"), 1, UtcNow);
        Assert.Equal(initialVersion + 2, cart.Version);

        cart.UpdateQuantity(variantA, 4, UtcNow);
        Assert.Equal(initialVersion + 3, cart.Version);

        cart.RemoveItem(variantB, UtcNow);
        Assert.Equal(initialVersion + 4, cart.Version);

        cart.Clear(UtcNow);
        Assert.Equal(initialVersion + 5, cart.Version);
    }

    [Fact]
    public void NoDuplicateVariantId_AfterSequenceOfAdds()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);
        var variantA = VariantId.Create();
        var variantB = VariantId.Create();

        for (var i = 0; i < 3; i++)
            cart.AddItem(ProductId.Create(), variantA, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);

        cart.AddItem(ProductId.Create(), variantB, new Sku("SKU-B"), "Product B", new Money(9.99m, "USD"), 1, UtcNow);

        var distinctVariantIds = cart.Items.Select(item => item.VariantId).Distinct().ToList();

        Assert.Equal(2, distinctVariantIds.Count);
        Assert.Equal(2, cart.Items.Count);
    }

    [Fact]
    public void Snapshot_RemainsIntact_WhenQuantityChanges()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);
        var productId = ProductId.Create();
        var variantId = VariantId.Create();
        var sku = new Sku("SKU-A");
        var unitPrice = new Money(19.99m, "USD");

        cart.AddItem(productId, variantId, sku, "Product A", unitPrice, 1, UtcNow);
        cart.UpdateQuantity(variantId, 6, UtcNow);

        var item = Assert.Single(cart.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(variantId, item.VariantId);
        Assert.Equal(sku, item.Sku);
        Assert.Equal("Product A", item.ProductName);
        Assert.Equal(unitPrice, item.UnitPrice);
        Assert.Equal(6, item.Quantity);
    }

    [Fact]
    public void Items_Order_IsDeterministicByInsertion()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);
        var variantA = VariantId.Create();
        var variantB = VariantId.Create();
        var variantC = VariantId.Create();

        cart.AddItem(ProductId.Create(), variantA, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);
        cart.AddItem(ProductId.Create(), variantB, new Sku("SKU-B"), "Product B", new Money(9.99m, "USD"), 1, UtcNow);
        cart.AddItem(ProductId.Create(), variantC, new Sku("SKU-C"), "Product C", new Money(5.99m, "USD"), 1, UtcNow);

        var orderedVariants = cart.Items.Select(item => item.VariantId).ToList();

        Assert.Equal(new[] { variantA, variantB, variantC }, orderedVariants);
    }

    [Fact]
    public void GuestOwnership_Unchanged_AfterMutations()
    {
        var cart = Cart.CreateGuest(CartId.Create(), UtcNow);
        var variantId = VariantId.Create();

        cart.AddItem(ProductId.Create(), variantId, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);
        cart.UpdateQuantity(variantId, 3, UtcNow);

        Assert.True(cart.IsGuest);
        Assert.Null(cart.UserId);
    }

    [Fact]
    public void AuthenticatedOwnership_Unchanged_AfterMutations()
    {
        var userId = Guid.NewGuid();
        var cart = Cart.CreateAuthenticated(CartId.Create(), userId, UtcNow);
        var variantId = VariantId.Create();

        cart.AddItem(ProductId.Create(), variantId, new Sku("SKU-A"), "Product A", new Money(19.99m, "USD"), 1, UtcNow);
        cart.UpdateQuantity(variantId, 3, UtcNow);

        Assert.True(cart.IsAuthenticated);
        Assert.Equal(userId, cart.UserId);
    }
}
