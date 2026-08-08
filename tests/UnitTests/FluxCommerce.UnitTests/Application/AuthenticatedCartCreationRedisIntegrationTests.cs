using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Verifies authenticated Cart creation against real Redis: correct UserId ownership persisted,
/// get-or-create idempotency for repeated creation, atomic one-active-Cart enforcement under
/// concurrent creation, and the persistence-level guard against guest state.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class AuthenticatedCartCreationRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public AuthenticatedCartCreationRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateAuthenticatedCart_PersistsCorrectOwnership_AndIsRetrievable()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var get = new GetAuthenticatedCartQueryHandler(store, currentUser);

        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var state = created.Value!;
        _fixture.Track(state);
        Assert.Equal(userId, state.UserId);
        Assert.NotEqual(Guid.Empty, state.CartId.Value);
        Assert.Equal(1, state.Version);
        Assert.Empty(state.Items);

        // Ownership is persisted: the Cart key document and the user index key both exist.
        Assert.True(await store.ExistsAsync(state.CartId));
        var indexValue = (await _fixture.Database.StringGetAsync(_fixture.UserKey(userId))).ToString();
        Assert.Equal(state.CartId.Value.ToString("D"), indexValue);

        var retrieved = await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None);
        Assert.True(retrieved.IsSuccess);
        RedisCartTestData.AssertCartStateEqual(state, retrieved.Value);
    }

    [Fact]
    public async Task RepeatedCreation_DoesNotCreateMultipleActiveCarts()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);

        var first = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(first.IsSuccess);
        _fixture.Track(first.Value!);
        var firstCart = first.Value!;

        var second = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(second.IsSuccess);
        var secondCart = second.Value!;

        Assert.Equal(firstCart.CartId, secondCart.CartId);
        Assert.Equal(userId, secondCart.UserId);

        // Exactly one active Cart is reachable through the user index.
        var resolved = await store.GetByUserIdAsync(userId);
        Assert.NotNull(resolved);
        Assert.Equal(firstCart.CartId, resolved!.CartId);
        Assert.Equal(userId, resolved.UserId);
    }

    [Fact]
    public async Task ConcurrentCreateForUser_IsAtomic_OnlyOneActiveCartWins()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var writers = 8;
        var states = Enumerable.Range(0, writers)
            .Select(_ => RedisCartTestData.Create(userId: userId))
            .ToArray();

        var results = await Task.WhenAll(states.Select(state => store.CreateForUserAsync(state)));

        Assert.Equal(1, results.Count(r => r.Status == CartSaveStatus.Success));
        Assert.Equal(writers - 1, results.Count(r => r.Status == CartSaveStatus.DuplicateActiveCart));

        var winner = results.Single(r => r.Status == CartSaveStatus.Success).Cart!;
        _fixture.Track(winner);

        var resolved = await store.GetByUserIdAsync(userId);
        Assert.NotNull(resolved);
        Assert.Equal(winner.CartId, resolved!.CartId);
        Assert.Equal(userId, resolved.UserId);
    }

    [Fact]
    public async Task ConcurrentCreateAuthenticatedCart_ReturnsOneActiveCart_ForEveryCaller()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser { UserId = userId, IsAuthenticated = true };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var writers = 8;

        var results = await Task.WhenAll(Enumerable.Range(0, writers).Select(_ =>
            create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None)));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        var cartIds = results.Select(r => r.Value!.CartId).Distinct().ToArray();
        Assert.Single(cartIds);

        var winner = results[0].Value!;
        _fixture.Track(winner);

        var resolved = await store.GetByUserIdAsync(userId);
        Assert.NotNull(resolved);
        Assert.Equal(winner.CartId, resolved!.CartId);
    }

    [Fact]
    public async Task CreateAuthenticatedCart_Unauthenticated_ReturnsAuthenticationRequired()
    {
        var store = _fixture.CreateStore();
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);

        var result = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
    }

    [Fact]
    public async Task CreateForUserAsync_RejectsGuestCartState()
    {
        var store = _fixture.CreateStore();
        var guest = RedisCartTestData.Create(userId: null);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateForUserAsync(guest));
    }
}
