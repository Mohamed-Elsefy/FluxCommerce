using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Services;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CartCheckoutSnapshotProviderTests
{
    [Fact]
    public async Task GetCheckoutSnapshotAsync_MissingCart_ReturnsNull()
    {
        var store = CreateStoreMock(loaded: null);
        var provider = new CartCheckoutSnapshotProvider(store.Object);

        var snapshot = await provider.GetCheckoutSnapshotAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetCheckoutSnapshotAsync_GuestCart_ExposesCartIdAndNullCustomer_WithoutCartIdentity()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 2, unitPrice: 24.99m);
        var store = CreateStoreMock(loaded);
        var provider = new CartCheckoutSnapshotProvider(store.Object);

        var snapshot = await provider.GetCheckoutSnapshotAsync(loaded.CartId.Value, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(loaded.CartId.Value, snapshot!.CartId);
        Assert.Null(snapshot.CustomerId);
    }

    [Fact]
    public async Task GetCheckoutSnapshotAsync_AuthenticatedCart_ExposesCustomerId()
    {
        var userId = Guid.NewGuid();
        var loaded = RedisCartTestData.Create(userId: userId);
        var store = CreateStoreMock(loaded);
        var provider = new CartCheckoutSnapshotProvider(store.Object);

        var snapshot = await provider.GetCheckoutSnapshotAsync(loaded.CartId.Value, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(userId, snapshot!.CustomerId);
    }

    [Fact]
    public async Task GetCheckoutSnapshotAsync_MapsItemSnapshot_FromCartState()
    {
        var loaded = RedisCartTestData.Create(userId: null, quantity: 3, unitPrice: 12.50m);
        var store = CreateStoreMock(loaded);
        var provider = new CartCheckoutSnapshotProvider(store.Object);

        var snapshot = await provider.GetCheckoutSnapshotAsync(loaded.CartId.Value, CancellationToken.None);

        Assert.NotNull(snapshot);
        var item = Assert.Single(snapshot!.Items);
        Assert.Equal(loaded.Items[0].ProductId.Value, item.ProductId);
        Assert.Equal(loaded.Items[0].VariantId.Value, item.VariantId);
        Assert.Equal(loaded.Items[0].Sku.Value, item.Sku);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(12.50m, item.UnitPriceAmount);
        Assert.Equal("USD", item.Currency);
    }

    [Fact]
    public async Task GetCheckoutSnapshotAsync_DoesNotExposeCartVersionOrTimestamps()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid(), version: 5);
        var store = CreateStoreMock(loaded);
        var provider = new CartCheckoutSnapshotProvider(store.Object);

        var snapshot = await provider.GetCheckoutSnapshotAsync(loaded.CartId.Value, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.IsType<CartCheckoutSnapshot>(snapshot);
        Assert.NotNull(snapshot!.Items);
    }

    [Fact]
    public async Task GetCheckoutSnapshotAsync_LooksUpCartThroughSingleStoreRead()
    {
        var loaded = RedisCartTestData.Create(userId: null);
        var store = CreateStoreMock(loaded);
        var provider = new CartCheckoutSnapshotProvider(store.Object);
        var cartId = loaded.CartId.Value;
        CartId? queried = null;
        store
            .Setup(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()))
            .Callback<CartId, CancellationToken>((id, _) => queried = id)
            .ReturnsAsync(loaded);

        await provider.GetCheckoutSnapshotAsync(cartId, CancellationToken.None);

        Assert.Equal(new CartId(cartId), queried);
        store.Verify(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<ICartStore> CreateStoreMock(CartState? loaded)
    {
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loaded);
        return store;
    }
}
