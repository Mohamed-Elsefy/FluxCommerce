using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class CreateAuthenticatedCartCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_CreatesAuthenticatedCart_WithUserIdFromCurrentUser()
    {
        var userId = Guid.NewGuid();
        CartState? created = null;
        var store = CreateStore(existing: null);
        store
            .Setup(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()))
            .Callback<CartState, CancellationToken>((state, _) => created = state)
            .ReturnsAsync((CartState state, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(created);
        Assert.Equal(userId, created!.UserId);
        Assert.NotEqual(Guid.Empty, created.CartId.Value);
        Assert.Equal(1, created.Version);
        Assert.Empty(created.Items);
        store.Verify(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UserIdComesFromICurrentUser_NotFromRequestData()
    {
        var userId = Guid.NewGuid();
        Guid? indexQueried = null;
        var store = CreateStore(existing: null);
        store
            .Setup(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => indexQueried = id)
            .ReturnsAsync((CartState?)null);
        store
            .Setup(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CartState state, CancellationToken _) => new CartSaveResult(CartSaveStatus.Success, state));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, indexQueried);
        Assert.Equal(userId, result.Value!.UserId);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired_AndCreatesNothing()
    {
        var store = CreateStore(existing: null);
        var currentUser = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var handler = new CreateAuthenticatedCartCommandHandler(store.Object, FixedDateTimeProvider.CreateDefault(), currentUser);

        var result = await handler.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
        store.Verify(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenUserAlreadyHasActiveCart_ReturnsExistingCart_WithoutCreating()
    {
        var userId = Guid.NewGuid();
        var existing = RedisCartTestData.Create(userId: userId, version: 4);
        var store = CreateStore(existing);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        RedisCartTestData.AssertCartStateEqual(existing, result.Value);
        store.Verify(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExistingGuestCartResolvedThroughIndex_ReturnsAccessDenied()
    {
        var userId = Guid.NewGuid();
        var guest = RedisCartTestData.Create(userId: null);
        var store = CreateStore(guest);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error.Code);
        store.Verify(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ConcurrentDuplicateCreate_ReturnsTheWinnerCart()
    {
        var userId = Guid.NewGuid();
        var winner = RedisCartTestData.Create(userId: userId);
        var store = CreateStore(existing: null);
        store
            .Setup(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.DuplicateActiveCart, null));
        store
            .Setup(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(winner);
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        RedisCartTestData.AssertCartStateEqual(winner, result.Value);
    }

    [Fact]
    public async Task HandleAsync_ConcurrentDuplicateCreate_WhenWinnerMissing_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(existing: null);
        store
            .Setup(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.DuplicateActiveCart, null));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_CreateConflict_ReturnsConflictError()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore(existing: null);
        store
            .Setup(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.VersionConflict, null));
        var handler = CreateHandler(store, userId);

        var result = await handler.HandleAsync(new CreateAuthenticatedCartCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    private static CreateAuthenticatedCartCommandHandler CreateHandler(
        Mock<ICartStore> store,
        Guid userId) =>
        new(
            store.Object,
            FixedDateTimeProvider.CreateDefault(),
            new FakeCurrentUser { UserId = userId, IsAuthenticated = true });

    private static Mock<ICartStore> CreateStore(CartState? existing)
    {
        var store = new Mock<ICartStore>();
        store
            .Setup(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        return store;
    }
}
