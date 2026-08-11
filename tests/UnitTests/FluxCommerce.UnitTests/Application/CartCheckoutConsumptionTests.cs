using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Services;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Phase 4.8 Cart consumption boundary coverage. The consumer is the post-commit tail of Checkout so
/// the tests pin down the exact contract the Orders flow relies on: it never throws, it only clears a
/// Cart whose persisted ownership matches the caller, and it reports every outcome as a status.
/// </summary>
public class CartCheckoutConsumptionTests
{
    [Fact]
    public async Task ConsumeAsync_MissingCart_ReturnsCartNotFound()
    {
        var store = CreateStoreMock(loaded: null);
        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.CartNotFound, result.Status);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeAsync_GuestCart_ReturnsAccessDenied()
    {
        var loaded = RedisCartTestData.Create(userId: null);
        var store = CreateStoreMock(loaded);
        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(loaded.CartId.Value, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.AccessDenied, result.Status);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeAsync_AnotherCustomersCart_ReturnsAccessDenied()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid());
        var store = CreateStoreMock(loaded);
        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(
            loaded.CartId.Value,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.AccessDenied, result.Status);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeAsync_AlreadyEmptyCart_ReturnsAlreadyEmpty_WithoutSaving()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid()) with
        {
            Items = [],
        };
        var store = CreateStoreMock(loaded);
        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(loaded.CartId.Value, loaded.UserId!.Value, CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.AlreadyEmpty, result.Status);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeAsync_OwnedCart_ClearsItems_SavesWithExpectedVersion_AndReturnsConsumed()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid(), version: 4, quantity: 3);
        var store = CreateStoreMock(loaded);

        CartState? saved = null;
        long? expectedVersion = null;
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, long, CancellationToken>((state, version, _) =>
            {
                saved = state;
                expectedVersion = version;
            })
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.Success, null));

        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(loaded.CartId.Value, loaded.UserId!.Value, CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.Consumed, result.Status);
        Assert.Equal(4, expectedVersion);
        Assert.NotNull(saved);
        Assert.Empty(saved!.Items);
        Assert.Equal(5, saved.Version);
        Assert.Equal(loaded.UserId, saved.UserId);
        store.Verify(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumeAsync_VersionConflict_ReturnsConcurrencyConflict()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid(), version: 2);
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.VersionConflict, null));

        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(loaded.CartId.Value, loaded.UserId!.Value, CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.ConcurrencyConflict, result.Status);
    }

    [Fact]
    public async Task ConsumeAsync_CartMissingOnSave_ReturnsFailed()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid());
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.CartMissing, null));

        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(loaded.CartId.Value, loaded.UserId!.Value, CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ConsumeAsync_StoreReadThrows_ReturnsFailed()
    {
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis down."));

        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ConsumeAsync_StoreSaveThrows_ReturnsFailed()
    {
        var loaded = RedisCartTestData.Create(userId: Guid.NewGuid());
        var store = CreateStoreMock(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis down."));

        var consumer = new CartCheckoutConsumption(store.Object, FixedDateTimeProvider.CreateDefault());

        var result = await consumer.ConsumeAsync(loaded.CartId.Value, loaded.UserId!.Value, CancellationToken.None);

        Assert.Equal(CartCheckoutConsumptionStatus.Failed, result.Status);
    }

    private static Mock<ICartStore> CreateStoreMock(CartState? loaded)
    {
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loaded);
        store
            .Setup(s => s.SaveAsync(It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.Success, loaded));
        return store;
    }
}