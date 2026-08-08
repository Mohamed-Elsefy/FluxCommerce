using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Verifies ownership/isolation for every Guest Cart mutation against real Redis:
/// Guest A can mutate cart A; a different identifier can never reach or affect Guest B's
/// cart; and a Guest operation on an authenticated Cart is always rejected.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class GuestCartOwnershipRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public GuestCartOwnershipRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WrongIdentifier_NeverMutates_AnotherGuestsCart()
    {
        var store = _fixture.CreateStore();
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var add = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var update = new UpdateGuestCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var remove = new RemoveGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        // Guest A creates cart A and populates it.
        var a = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(a.IsSuccess);
        _fixture.Track(a.Value!);
        var cartA = a.Value!;
        var variantInA = Guid.NewGuid();
        var addA = await add.HandleAsync(
            new AddGuestCartItemCommand(cartA.CartId.Value, variantInA, Guid.NewGuid(), "SKU-A", "Product A", 5m, "USD", 1),
            CancellationToken.None);
        Assert.True(addA.IsSuccess);

        // Guest B creates cart B and populates it.
        var b = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(b.IsSuccess);
        _fixture.Track(b.Value!);
        var cartB = b.Value!;
        var variantInB = Guid.NewGuid();
        var addB = await add.HandleAsync(
            new AddGuestCartItemCommand(cartB.CartId.Value, variantInB, Guid.NewGuid(), "SKU-B", "Product B", 5m, "USD", 1),
            CancellationToken.None);
        Assert.True(addB.IsSuccess);
        var cartBBefore = (await store.GetAsync(cartB.CartId))!;

        // Guest A mutates BOTH carts with its own identifier: B must be completely unaffected.
        var mutatingB = await add.HandleAsync(
            new AddGuestCartItemCommand(cartA.CartId.Value, Guid.NewGuid(), Guid.NewGuid(), "SKU-X", "Product X", 1m, "USD", 1),
            CancellationToken.None);
        Assert.True(mutatingB.IsSuccess);
        var cartBAfterAdd = (await store.GetAsync(cartB.CartId))!;
        RedisCartTestData.AssertCartStateEqual(cartBBefore, cartBAfterAdd);

        // A guessed/foreign identifier for cart B never resolves, for every mutation type.
        var wrongIdentifier = Guid.NewGuid();
        var results = new[]
        {
            (await add.HandleAsync(new AddGuestCartItemCommand(wrongIdentifier, Guid.NewGuid(), Guid.NewGuid(), "SKU-X", "Product X", 1m, "USD", 1), CancellationToken.None)).Error?.Code,
            (await update.HandleAsync(new UpdateGuestCartItemQuantityCommand(wrongIdentifier, variantInB, 5), CancellationToken.None)).Error?.Code,
            (await remove.HandleAsync(new RemoveGuestCartItemCommand(wrongIdentifier, variantInB), CancellationToken.None)).Error?.Code,
            (await clear.HandleAsync(new ClearGuestCartCommand(wrongIdentifier), CancellationToken.None)).Error?.Code,
        };

        Assert.All(results, code => Assert.Equal("Cart.CartNotFound", code));

        var cartBAfter = (await store.GetAsync(cartB.CartId))!;
        RedisCartTestData.AssertCartStateEqual(cartBBefore, cartBAfter);
    }

    [Fact]
    public async Task GuestOperations_OnAuthenticatedCart_AreRejected_ForEveryMutation()
    {
        var store = _fixture.CreateStore();
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var add = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var update = new UpdateGuestCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var remove = new RemoveGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var authenticated = RedisCartTestData.Create(userId: Guid.NewGuid(), quantity: 2);
        _fixture.Track(authenticated);
        var saved = await store.SaveAsync(authenticated, expectedVersion: 0);
        Assert.Equal(CartSaveStatus.Success, saved.Status);
        var cartId = authenticated.CartId.Value;
        var variantId = authenticated.Items[0].VariantId.Value;

        var results = new[]
        {
            (await add.HandleAsync(new AddGuestCartItemCommand(cartId, Guid.NewGuid(), Guid.NewGuid(), "SKU-X", "Product X", 1m, "USD", 1), CancellationToken.None)).Error?.Code,
            (await update.HandleAsync(new UpdateGuestCartItemQuantityCommand(cartId, variantId, 5), CancellationToken.None)).Error?.Code,
            (await remove.HandleAsync(new RemoveGuestCartItemCommand(cartId, variantId), CancellationToken.None)).Error?.Code,
            (await clear.HandleAsync(new ClearGuestCartCommand(cartId), CancellationToken.None)).Error?.Code,
        };

        Assert.All(results, code => Assert.Equal("Cart.CartAccessDenied", code));

        var unchanged = (await store.GetAsync(authenticated.CartId))!;
        RedisCartTestData.AssertCartStateEqual(authenticated, unchanged);
    }

    [Fact]
    public async Task EachGuest_CanMutateTheirOwnCart()
    {
        var store = _fixture.CreateStore();
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var add = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        var a = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        var b = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
        _fixture.Track(a.Value!);
        _fixture.Track(b.Value!);

        var addA = await add.HandleAsync(
            new AddGuestCartItemCommand(a.Value!.CartId.Value, Guid.NewGuid(), Guid.NewGuid(), "SKU-A", "Product A", 5m, "USD", 1),
            CancellationToken.None);
        var addB = await add.HandleAsync(
            new AddGuestCartItemCommand(b.Value!.CartId.Value, Guid.NewGuid(), Guid.NewGuid(), "SKU-B", "Product B", 5m, "USD", 1),
            CancellationToken.None);

        Assert.True(addA.IsSuccess);
        Assert.True(addB.IsSuccess);
        Assert.Single((await store.GetAsync(a.Value.CartId))!.Items);
        Assert.Single((await store.GetAsync(b.Value.CartId))!.Items);
    }
}
