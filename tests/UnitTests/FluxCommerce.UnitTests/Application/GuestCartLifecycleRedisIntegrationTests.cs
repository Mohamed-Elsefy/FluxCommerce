using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Exercises the complete Guest Cart lifecycle end to end against real Redis:
/// create -> retrieve -> add -> verify -> update -> verify -> remove -> verify -> add again -> clear
/// -> verify empty -> expire -> behaves as missing -> explicit re-creation.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class GuestCartLifecycleRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public GuestCartLifecycleRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompleteGuestCartLifecycle_EndToEnd()
    {
        var store = _fixture.CreateStore(defaultTtlMinutes: 1);
        var create = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var get = new GetGuestCartQueryHandler(store);
        var add = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), FakeCatalogVariantCartLookup.Available());
        var update = new UpdateGuestCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var remove = new RemoveGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var clear = new ClearGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());

        // 1-2. Generate an opaque Guest Cart identity and create the cart.
        var created = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        var createdState = created.Value!;
        Assert.NotEqual(Guid.Empty, createdState.CartId.Value);
        Assert.Null(createdState.UserId);
        Assert.Empty(createdState.Items);
        Assert.Equal(1, createdState.Version);
        _fixture.Track(createdState);

        var cartId = createdState.CartId.Value;

        // 3. Retrieve the freshly created Guest Cart.
        var initial = await get.HandleAsync(new GetGuestCartQuery(cartId), CancellationToken.None);
        Assert.True(initial.IsSuccess);
        RedisCartTestData.AssertCartStateEqual(createdState, initial.Value);

        // 4. Add two items.
        var variantA = Guid.NewGuid();
        var variantB = Guid.NewGuid();
        var addA = await add.HandleAsync(
            new AddGuestCartItemCommand(cartId, variantA, 10.00m, "USD", 2),
            CancellationToken.None);
        Assert.True(addA.IsSuccess);
        Assert.Equal(2, addA.Value!.Version);
        var addB = await add.HandleAsync(
            new AddGuestCartItemCommand(cartId, variantB, 20.00m, "USD", 1),
            CancellationToken.None);
        Assert.True(addB.IsSuccess);
        Assert.Equal(3, addB.Value!.Version);

        // 5. Retrieve and verify both items.
        var withItems = await get.HandleAsync(new GetGuestCartQuery(cartId), CancellationToken.None);
        Assert.True(withItems.IsSuccess);
        Assert.Equal(2, withItems.Value!.Items.Count);

        // 6-7. Update quantity and verify.
        var updated = await update.HandleAsync(
            new UpdateGuestCartItemQuantityCommand(cartId, variantA, 5),
            CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal(5, Assert.Single(updated.Value!.Items, i => i.VariantId == new VariantId(variantA)).Quantity);
        Assert.Equal(4, updated.Value.Version);
        var updatedRetrieved = await get.HandleAsync(new GetGuestCartQuery(cartId), CancellationToken.None);
        Assert.Equal(5, Assert.Single(updatedRetrieved.Value!.Items, i => i.VariantId == new VariantId(variantA)).Quantity);

        // 8-9. Remove one item and verify.
        var removed = await remove.HandleAsync(
            new RemoveGuestCartItemCommand(cartId, variantA),
            CancellationToken.None);
        Assert.True(removed.IsSuccess);
        Assert.Equal(5, removed.Value!.Version);
        Assert.Single(removed.Value.Items);
        Assert.DoesNotContain(removed.Value.Items, i => i.VariantId == new VariantId(variantA));
        var afterRemove = await get.HandleAsync(new GetGuestCartQuery(cartId), CancellationToken.None);
        Assert.Single(afterRemove.Value!.Items);

        // 10. Add items again.
        var reAdd = await add.HandleAsync(
            new AddGuestCartItemCommand(cartId, variantA, 10.00m, "USD", 3),
            CancellationToken.None);
        Assert.True(reAdd.IsSuccess);
        Assert.Equal(6, reAdd.Value!.Version);
        Assert.Equal(2, reAdd.Value!.Items.Count);

        // 11-12. Clear the cart and verify it is empty with identity/ownership preserved.
        var cleared = await clear.HandleAsync(new ClearGuestCartCommand(cartId), CancellationToken.None);
        Assert.True(cleared.IsSuccess);
        Assert.Empty(cleared.Value!.Items);
        Assert.Equal(7, cleared.Value.Version);
        Assert.Equal(createdState.CartId, cleared.Value.CartId);
        Assert.Null(cleared.Value.UserId);
        var clearedRetrieved = await get.HandleAsync(new GetGuestCartQuery(cartId), CancellationToken.None);
        Assert.True(clearedRetrieved.IsSuccess);
        Assert.Empty(clearedRetrieved.Value!.Items);

        // 13-14. Force expiration of the persisted key and let Redis remove it.
        var cartKey = _fixture.CartKey(createdState.CartId);
        await _fixture.Database.KeyExpireAsync(cartKey, TimeSpan.FromMilliseconds(300));
        await Task.Delay(TimeSpan.FromMilliseconds(900));

        // 15. Expired Cart behaves as missing: Get returns CartNotFound, Exists is false.
        var expired = await get.HandleAsync(new GetGuestCartQuery(cartId), CancellationToken.None);
        Assert.True(expired.IsFailure);
        Assert.Equal("Cart.CartNotFound", expired.Error.Code);
        Assert.False(await store.ExistsAsync(createdState.CartId));
        Assert.Null(await store.GetAsync(createdState.CartId));

        // Expired Cart is not resurrected by mutations.
        var addAfterExpiry = await add.HandleAsync(
            new AddGuestCartItemCommand(cartId, Guid.NewGuid(), 1m, "USD", 1),
            CancellationToken.None);
        Assert.True(addAfterExpiry.IsFailure);
        Assert.Equal("Cart.CartNotFound", addAfterExpiry.Error.Code);
        Assert.False(await store.ExistsAsync(createdState.CartId));

        // 16-17. Explicit re-creation produces a new, valid Guest Cart with a fresh identity.
        var recreated = await create.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(recreated.IsSuccess);
        var recreatedState = recreated.Value!;
        Assert.NotEqual(createdState.CartId, recreatedState.CartId);
        Assert.Null(recreatedState.UserId);
        Assert.Empty(recreatedState.Items);
        Assert.Equal(1, recreatedState.Version);
        _fixture.Track(recreatedState);
    }
}
