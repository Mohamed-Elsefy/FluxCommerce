using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateAuthenticatedCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Verifies User ownership and Guest/Authenticated isolation against real Redis:
/// User A can access only A's Cart, User B can never access or mutate A's Cart, and neither
/// ownership path can accidentally resolve the other.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class AuthenticatedCartOwnershipRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public AuthenticatedCartOwnershipRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserB_CannotAccessOrMutate_UserACart()
    {
        var store = _fixture.CreateStore();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var currentUserA = new FakeCurrentUser { UserId = userA, IsAuthenticated = true };
        var currentUserB = new FakeCurrentUser { UserId = userB, IsAuthenticated = true };

        var createA = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserA);
        var addA = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserA, FakeCatalogVariantCartLookup.Available());
        var createdA = await createA.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(createdA.IsSuccess);
        _fixture.Track(createdA.Value!);
        var cartA = createdA.Value!;
        var variantInA = Guid.NewGuid();
        var addAResult = await addA.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantInA, 5m, "USD", 1),
            CancellationToken.None);
        Assert.True(addAResult.IsSuccess);
        var cartABefore = (await store.GetAsync(cartA.CartId))!;

        // User B has no active Cart: retrieval and every mutation resolve B's own (missing) index.
        var getB = new GetAuthenticatedCartQueryHandler(store, currentUserB);
        var addB = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB, FakeCatalogVariantCartLookup.Available());
        var updateB = new UpdateAuthenticatedCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB);
        var removeB = new RemoveAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB);
        var clearB = new ClearAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB);

        var results = new[]
        {
            (await getB.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None)).Error?.Code,
            (await addB.HandleAsync(new AddAuthenticatedCartItemCommand(Guid.NewGuid(), 1m, "USD", 1), CancellationToken.None)).Error?.Code,
            (await updateB.HandleAsync(new UpdateAuthenticatedCartItemQuantityCommand(variantInA, 5), CancellationToken.None)).Error?.Code,
            (await removeB.HandleAsync(new RemoveAuthenticatedCartItemCommand(variantInA), CancellationToken.None)).Error?.Code,
            (await clearB.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None)).Error?.Code,
        };

        Assert.All(results, code => Assert.Equal("Cart.CartNotFound", code));

        var cartAAfter = (await store.GetAsync(cartA.CartId))!;
        RedisCartTestData.AssertCartStateEqual(cartABefore, cartAAfter);
    }

    [Fact]
    public async Task AuthenticatedCart_IsNotReachable_ThroughGuestPath()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser, FakeCatalogVariantCartLookup.Available());

        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);
        var cartId = created.Value!.CartId.Value;
        var addResult = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), 5m, "USD", 1),
            CancellationToken.None);
        Assert.True(addResult.IsSuccess);

        var guestAdd = new AddGuestCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), FakeCatalogVariantCartLookup.Available());
        var guestGet = new GetGuestCartQueryHandler(store);

        var addCode = (await guestAdd.HandleAsync(
            new AddGuestCartItemCommand(cartId, Guid.NewGuid(), 1m, "USD", 1),
            CancellationToken.None)).Error?.Code;
        var getCode = (await guestGet.HandleAsync(new GetGuestCartQuery(cartId), CancellationToken.None)).Error?.Code;

        Assert.Equal("Cart.CartAccessDenied", addCode);
        Assert.Equal("Cart.CartAccessDenied", getCode);
    }

    [Fact]
    public async Task GuestCart_IsNotReachable_ThroughAuthenticatedPath()
    {
        var store = _fixture.CreateStore();
        var createGuest = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var createdGuest = await createGuest.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);
        Assert.True(createdGuest.IsSuccess);
        _fixture.Track(createdGuest.Value!);
        var guestCartId = createdGuest.Value!.CartId;

        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var get = new GetAuthenticatedCartQueryHandler(store, currentUser);

        // The user index is empty for this user: the Guest Cart is never returned as an
        // authenticated Cart, and it remains intact.
        var result = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        Assert.True(await store.ExistsAsync(guestCartId));
    }

    [Fact]
    public async Task TwoUsers_EachCanOnlyAccessOwnCart()
    {
        var store = _fixture.CreateStore();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var currentUserA = new FakeCurrentUser { UserId = userA, IsAuthenticated = true };
        var currentUserB = new FakeCurrentUser { UserId = userB, IsAuthenticated = true };

        var createA = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserA);
        var createB = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB);
        var addA = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserA, FakeCatalogVariantCartLookup.Available());
        var addB = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB, FakeCatalogVariantCartLookup.Available());
        var getA = new GetAuthenticatedCartQueryHandler(store, currentUserA);
        var getB = new GetAuthenticatedCartQueryHandler(store, currentUserB);

        // Each user creates and populates their own Cart.
        var createdA = await createA.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(createdA.IsSuccess);
        _fixture.Track(createdA.Value!);
        var variantInA = Guid.NewGuid();
        var addAResult = await addA.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantInA, 5m, "USD", 1),
            CancellationToken.None);
        Assert.True(addAResult.IsSuccess);

        var createdB = await createB.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(createdB.IsSuccess);
        _fixture.Track(createdB.Value!);
        var variantInB = Guid.NewGuid();
        var addBResult = await addB.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantInB, 9m, "USD", 2),
            CancellationToken.None);
        Assert.True(addBResult.IsSuccess);

        // Distinct Carts with distinct persisted ownership.
        Assert.NotEqual(createdA.Value!.CartId, createdB.Value!.CartId);
        Assert.Equal(userA, (await store.GetAsync(createdA.Value.CartId))!.UserId);
        Assert.Equal(userB, (await store.GetAsync(createdB.Value.CartId))!.UserId);

        // A can access only A's Cart; B can access only B's Cart.
        var resolvedA = (await getA.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None)).Value!;
        var resolvedB = (await getB.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None)).Value!;
        Assert.Equal(createdA.Value.CartId, resolvedA.CartId);
        Assert.Equal(createdB.Value.CartId, resolvedB.CartId);
        Assert.Single(resolvedA.Items, i => i.VariantId == new VariantId(variantInA));
        Assert.Single(resolvedB.Items, i => i.VariantId == new VariantId(variantInB));

        // Neither Cart leaks the other user's item.
        Assert.DoesNotContain(resolvedA.Items, i => i.VariantId == new VariantId(variantInB));
        Assert.DoesNotContain(resolvedB.Items, i => i.VariantId == new VariantId(variantInA));

        // The user index never resolves the other user's Cart.
        Assert.Equal(createdA.Value.CartId, (await store.GetByUserIdAsync(userA))!.CartId);
        Assert.Equal(createdB.Value.CartId, (await store.GetByUserIdAsync(userB))!.CartId);
    }

    [Fact]
    public async Task CrossUserAccess_ThroughCorruptUserIndex_IsDenied_AndTargetCartUnchanged()
    {
        var store = _fixture.CreateStore();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var currentUserA = new FakeCurrentUser { UserId = userA, IsAuthenticated = true };
        var currentUserB = new FakeCurrentUser { UserId = userB, IsAuthenticated = true };

        var createA = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserA);
        var addA = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserA, FakeCatalogVariantCartLookup.Available());
        var createdA = await createA.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(createdA.IsSuccess);
        _fixture.Track(createdA.Value!);
        var variantInA = Guid.NewGuid();
        var addAResult = await addA.HandleAsync(
            new AddAuthenticatedCartItemCommand(variantInA, 5m, "USD", 1),
            CancellationToken.None);
        Assert.True(addAResult.IsSuccess);
        var cartA = createdA.Value!;
        var cartABefore = (await store.GetAsync(cartA.CartId))!;

        // Corrupt User B's index so it points at User A's Cart key: even when a lookup could
        // resolve a foreign Cart, the persisted-ownership boundary must reject every operation.
        _fixture.Track(_fixture.UserKey(userB));
        await _fixture.Database.StringSetAsync(_fixture.UserKey(userB), cartA.CartId.Value.ToString("D"));

        var getB = new GetAuthenticatedCartQueryHandler(store, currentUserB);
        var addB = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB, FakeCatalogVariantCartLookup.Available());
        var updateB = new UpdateAuthenticatedCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB);
        var removeB = new RemoveAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB);
        var clearB = new ClearAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUserB);

        var results = new[]
        {
            (await getB.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None)).Error?.Code,
            (await addB.HandleAsync(new AddAuthenticatedCartItemCommand(Guid.NewGuid(), 1m, "USD", 1), CancellationToken.None)).Error?.Code,
            (await updateB.HandleAsync(new UpdateAuthenticatedCartItemQuantityCommand(variantInA, 5), CancellationToken.None)).Error?.Code,
            (await removeB.HandleAsync(new RemoveAuthenticatedCartItemCommand(variantInA), CancellationToken.None)).Error?.Code,
            (await clearB.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None)).Error?.Code,
        };

        // Every operation is rejected by the persisted-ownership validation.
        Assert.All(results, code => Assert.Equal("Cart.CartAccessDenied", code));

        // The ownership failure must not mutate the target Cart.
        var cartAAfter = (await store.GetAsync(cartA.CartId))!;
        RedisCartTestData.AssertCartStateEqual(cartABefore, cartAAfter);
    }
}
