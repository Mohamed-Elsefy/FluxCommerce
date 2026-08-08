using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateAuthenticatedCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Phase 3.5.3 error &amp; edge-case verification for the authenticated Cart against real Redis.
/// Every assertion uses the stable machine-readable error codes rather than implementation details.
/// </summary>
[Collection(RedisCartTestCollection.Name)]
public class AuthenticatedCartErrorsRedisIntegrationTests
{
    private readonly RedisCartFixture _fixture;

    public AuthenticatedCartErrorsRedisIntegrationTests(RedisCartFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Unauthenticated_EveryAuthenticatedOperation_ReturnsAuthenticationRequired()
    {
        var store = _fixture.CreateStore();
        var anonymous = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), anonymous);
        var get = new GetAuthenticatedCartQueryHandler(store, anonymous);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), anonymous);
        var update = new UpdateAuthenticatedCartItemQuantityCommandHandler(store, FixedDateTimeProvider.CreateDefault(), anonymous);
        var remove = new RemoveAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), anonymous);
        var clear = new ClearAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), anonymous);

        var results = new[]
        {
            await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None),
            await get.HandleAsync(new GetAuthenticatedCartQuery(), CancellationToken.None),
            await add.HandleAsync(new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-A", "Product A", 5m, "USD", 1), CancellationToken.None),
            await update.HandleAsync(new UpdateAuthenticatedCartItemQuantityCommand(Guid.NewGuid(), 1), CancellationToken.None),
            await remove.HandleAsync(new RemoveAuthenticatedCartItemCommand(Guid.NewGuid()), CancellationToken.None),
            await clear.HandleAsync(new ClearAuthenticatedCartCommand(), CancellationToken.None),
        };

        foreach (var result in results)
        {
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
            Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
        }
    }

    [Fact]
    public async Task AddItem_InvalidQuantity_ReturnsValidation_AndDoesNotMutate()
    {
        var store = _fixture.CreateStore();
        var (_, add, cartId) = await CreateCartWithItemAsync(store);
        var before = (await store.GetAsync(cartId))!;

        var result = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-A", "Product A", 5m, "USD", 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Cart.InvalidQuantity", result.Error.Code);

        var after = (await store.GetAsync(cartId))!;
        RedisCartTestData.AssertCartStateEqual(before, after);
    }

    [Fact]
    public async Task UpdateQuantity_InvalidQuantity_ReturnsValidation_AndDoesNotMutate()
    {
        var store = _fixture.CreateStore();
        var (_, _, cartId) = await CreateCartWithItemAsync(store);
        var before = (await store.GetAsync(cartId))!;
        var variantX = before.Items[0].VariantId.Value;
        var update = new UpdateAuthenticatedCartItemQuantityCommandHandler(
            store,
            FixedDateTimeProvider.CreateDefault(),
            CreateUser(before.UserId!.Value));

        var result = await update.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(variantX, 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Cart.InvalidQuantity", result.Error.Code);

        var after = (await store.GetAsync(cartId))!;
        RedisCartTestData.AssertCartStateEqual(before, after);
    }

    [Fact]
    public async Task UpdateQuantity_MissingItem_ReturnsCartItemNotFound_AndDoesNotMutate()
    {
        var store = _fixture.CreateStore();
        var (_, _, cartId) = await CreateCartWithItemAsync(store);
        var before = (await store.GetAsync(cartId))!;
        var update = new UpdateAuthenticatedCartItemQuantityCommandHandler(
            store,
            FixedDateTimeProvider.CreateDefault(),
            CreateUser(before.UserId!.Value));

        var result = await update.HandleAsync(
            new UpdateAuthenticatedCartItemQuantityCommand(Guid.NewGuid(), 3),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Cart.CartItemNotFound", result.Error.Code);

        var after = (await store.GetAsync(cartId))!;
        RedisCartTestData.AssertCartStateEqual(before, after);
    }

    [Fact]
    public async Task RemoveItem_MissingItem_ReturnsCartItemNotFound_AndDoesNotMutate()
    {
        var store = _fixture.CreateStore();
        var (_, _, cartId) = await CreateCartWithItemAsync(store);
        var before = (await store.GetAsync(cartId))!;
        var remove = new RemoveAuthenticatedCartItemCommandHandler(
            store,
            FixedDateTimeProvider.CreateDefault(),
            CreateUser(before.UserId!.Value));

        var result = await remove.HandleAsync(
            new RemoveAuthenticatedCartItemCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Cart.CartItemNotFound", result.Error.Code);

        var after = (await store.GetAsync(cartId))!;
        RedisCartTestData.AssertCartStateEqual(before, after);
    }

    [Fact]
    public async Task AddItem_InvalidSnapshotData_ReturnsValidation_AndDoesNotMutate()
    {
        var store = _fixture.CreateStore();
        var (_, add, cartId) = await CreateCartWithItemAsync(store);
        var before = (await store.GetAsync(cartId))!;

        // Blank product name.
        var blankName = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-A", "   ", 5m, "USD", 1),
            CancellationToken.None);
        // Empty Variant identifier.
        var emptyVariant = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.Empty, Guid.NewGuid(), "SKU-A", "Product A", 5m, "USD", 1),
            CancellationToken.None);
        // Invalid currency code.
        var invalidCurrency = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-A", "Product A", 5m, "US", 1),
            CancellationToken.None);

        Assert.All(new[] { blankName, emptyVariant, invalidCurrency }, result =>
        {
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Validation, result.Error!.Type);
            Assert.Equal("Cart.InvalidCartItem", result.Error.Code);
        });

        var after = (await store.GetAsync(cartId))!;
        RedisCartTestData.AssertCartStateEqual(before, after);
    }

    [Fact]
    public async Task AddExistingVariant_AccumulatesQuantity_NotDuplicateItem()
    {
        var store = _fixture.CreateStore();
        var (_, add, cartId) = await CreateCartWithItemAsync(store);
        var item = (await store.GetAsync(cartId))!.Items[0];

        var first = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(item.VariantId.Value, item.ProductId.Value, item.Sku.Value, item.ProductName, item.UnitPrice.Amount, item.UnitPrice.Currency, 2),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Single(first.Value!.Items);
        Assert.Equal(4, first.Value.Items[0].Quantity);
        Assert.Equal(3, first.Value.Version);

        var second = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(item.VariantId.Value, item.ProductId.Value, item.Sku.Value, item.ProductName, item.UnitPrice.Amount, item.UnitPrice.Currency, 3),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Single(second.Value!.Items);
        Assert.Equal(7, second.Value.Items[0].Quantity);
        Assert.Equal(4, second.Value.Version);
    }

    [Fact]
    public async Task AddItem_WhenNoActiveCart_ReturnsCartNotFound_AndDoesNotCreateImplicitly()
    {
        var store = _fixture.CreateStore();
        var userId = Guid.NewGuid();
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), CreateUser(userId));

        var result = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-A", "Product A", 5m, "USD", 1),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        Assert.Null(await store.GetByUserIdAsync(userId));
    }

    private async Task<(CreateAuthenticatedCartCommandHandler Create, AddAuthenticatedCartItemCommandHandler Add, CartId CartId)> CreateCartWithItemAsync(ICartStore store)
    {
        var userId = Guid.NewGuid();
        var currentUser = CreateUser(userId);
        var create = new CreateAuthenticatedCartCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);
        var add = new AddAuthenticatedCartItemCommandHandler(store, FixedDateTimeProvider.CreateDefault(), currentUser);

        var created = await create.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);
        _fixture.Track(created.Value!);

        var added = await add.HandleAsync(
            new AddAuthenticatedCartItemCommand(Guid.NewGuid(), Guid.NewGuid(), "SKU-X", "Product X", 10m, "USD", 2),
            CancellationToken.None);
        Assert.True(added.IsSuccess);

        return (create, add, created.Value!.CartId);
    }

    private static FakeCurrentUser CreateUser(Guid userId) =>
        new() { UserId = userId, IsAuthenticated = true };
}
