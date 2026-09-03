using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

[Collection(RedisCartTestCollection.Name)]
public class GuestCartApplicationRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public GuestCartApplicationRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateGuestCart_PersistsAndRetrieves_ThroughApplicationHandlers()
    {
        var store = _fixture.CreateStore();
        var createHandler = new CreateGuestCartCommandHandler(store, FixedDateTimeProvider.CreateDefault());
        var getHandler = new GetGuestCartQueryHandler(store);

        var created = await createHandler.HandleAsync(new CreateGuestCartCommand(), CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.NotNull(created.Value);
        _fixture.Track(created.Value!);

        var createdState = created.Value!;
        Assert.NotEqual(Guid.Empty, createdState.CartId.Value);
        Assert.Null(createdState.UserId);
        Assert.Empty(createdState.Items);
        Assert.Equal(1, createdState.Version);

        var retrieved = await getHandler.HandleAsync(
            new GetGuestCartQuery(createdState.CartId.Value),
            CancellationToken.None);

        Assert.True(retrieved.IsSuccess);
        Assert.NotNull(retrieved.Value);
        RedisCartTestData.AssertCartStateEqual(createdState, retrieved.Value);
    }

    [Fact]
    public async Task GetGuestCart_MissingCart_ReturnsCartNotFound()
    {
        var store = _fixture.CreateStore();
        var handler = new GetGuestCartQueryHandler(store);

        var result = await handler.HandleAsync(new GetGuestCartQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetGuestCart_AuthenticatedCart_IsRejectedWithAccessDenied()
    {
        var store = _fixture.CreateStore();
        var handler = new GetGuestCartQueryHandler(store);

        var userId = Guid.NewGuid();
        var authenticated = RedisCartTestData.Create(userId: userId);
        _fixture.Track(authenticated);
        await store.SaveAsync(authenticated, expectedVersion: 0);

        var result = await handler.HandleAsync(
            new GetGuestCartQuery(authenticated.CartId.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task GetGuestCart_DoesNotCreateACartImplicitly()
    {
        var store = _fixture.CreateStore();
        var handler = new GetGuestCartQueryHandler(store);
        var cartId = new CartId(Guid.NewGuid());

        var result = await handler.HandleAsync(new GetGuestCartQuery(cartId.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        Assert.False(await store.ExistsAsync(cartId));
    }
}
