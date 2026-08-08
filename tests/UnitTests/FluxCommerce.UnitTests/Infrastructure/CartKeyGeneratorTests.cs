using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class CartKeyGeneratorTests
{
    [Fact]
    public void ForCart_UsesDefaultNamespace_AndCartGuid()
    {
        var cartId = new CartId(Guid.NewGuid());
        var generator = CreateGenerator("cart");

        var key = generator.ForCart(cartId);

        Assert.Equal($"cart:{cartId.Value:D}", key);
    }

    [Fact]
    public void ForUser_UsesDefaultNamespace_WithUserSegment()
    {
        var userId = Guid.NewGuid();
        var generator = CreateGenerator("cart");

        var key = generator.ForUser(userId);

        Assert.Equal($"cart:user:{userId:D}", key);
    }

    [Fact]
    public void ForCart_UsesConfiguredNamespace()
    {
        var cartId = new CartId(Guid.NewGuid());
        var generator = CreateGenerator("storefront-cart");

        var key = generator.ForCart(cartId);

        Assert.Equal($"storefront-cart:{cartId.Value:D}", key);
    }

    [Fact]
    public void ForUser_UsesConfiguredNamespace()
    {
        var userId = Guid.NewGuid();
        var generator = CreateGenerator("storefront-cart");

        var key = generator.ForUser(userId);

        Assert.Equal($"storefront-cart:user:{userId:D}", key);
    }

    [Fact]
    public void ForCart_DistinctCartIds_ProduceDistinctKeys()
    {
        var generator = CreateGenerator("cart");

        var first = generator.ForCart(new CartId(Guid.NewGuid()));
        var second = generator.ForCart(new CartId(Guid.NewGuid()));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ForUser_DistinctUserIds_ProduceDistinctKeys()
    {
        var generator = CreateGenerator("cart");

        var first = generator.ForUser(Guid.NewGuid());
        var second = generator.ForUser(Guid.NewGuid());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ForCart_And_ForUser_Keys_AreDistinct()
    {
        var id = Guid.NewGuid();
        var generator = CreateGenerator("cart");

        Assert.NotEqual(generator.ForCart(new CartId(id)), generator.ForUser(id));
    }

    [Fact]
    public void ForCart_IsDeterministic_ForSameCartId()
    {
        var cartId = new CartId(Guid.NewGuid());
        var generator = CreateGenerator("cart");

        Assert.Equal(generator.ForCart(cartId), generator.ForCart(cartId));
    }

    [Fact]
    public void ForUser_IsDeterministic_ForSameUserId()
    {
        var userId = Guid.NewGuid();
        var generator = CreateGenerator("cart");

        Assert.Equal(generator.ForUser(userId), generator.ForUser(userId));
    }

    [Fact]
    public void ForCart_DifferentNamespaces_ProduceDistinctKeys()
    {
        var cartId = new CartId(Guid.NewGuid());

        var first = CreateGenerator("cart").ForCart(cartId);
        var second = CreateGenerator("storefront-cart").ForCart(cartId);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ForUser_DifferentNamespaces_ProduceDistinctKeys()
    {
        var userId = Guid.NewGuid();

        var first = CreateGenerator("cart").ForUser(userId);
        var second = CreateGenerator("storefront-cart").ForUser(userId);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ForCart_KeyDoesNotContainVersion()
    {
        var cartId = new CartId(Guid.NewGuid());
        var generator = CreateGenerator("cart");

        var key = generator.ForCart(cartId);

        Assert.Equal($"cart:{cartId.Value:D}", key);
        Assert.DoesNotContain("version", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForUser_EmptyUserId_Throws()
    {
        var generator = CreateGenerator("cart");

        Assert.Throws<ArgumentException>(() => generator.ForUser(Guid.Empty));
    }

    private static CartKeyGenerator CreateGenerator(string keyNamespace) =>
        new(Options.Create(new CartOptions { KeyNamespace = keyNamespace }));
}
