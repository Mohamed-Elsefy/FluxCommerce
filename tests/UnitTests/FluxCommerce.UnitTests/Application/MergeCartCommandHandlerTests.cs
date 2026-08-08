using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Application.Commands.MergeCart;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Phase 3.6.2: unit tests for the Guest → Authenticated merge handler with a mocked store.
/// The target is persisted and the Guest source is consumed through the single atomic
/// ICartStore.MergeAndConsumeAsync operation. Every assertion uses the stable machine-readable
/// error codes.
/// </summary>
public class MergeCartCommandHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly Guid GuestCartId = Guid.NewGuid();

    private static readonly Guid VariantA = Guid.NewGuid();

    private static readonly Guid VariantB = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_MergesGuestItems_IntoExistingAuthenticatedCart_AndConsumesGuestCart()
    {
        var existingTarget = AuthenticatedCart(userId: null, version: 3, Item(VariantB, 3));
        var store = CreateStoreMock(
            source: GuestCart(Item(VariantA, 2), Item(VariantB, 1)),
            existingTarget: existingTarget,
            saved: null);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var merged = result.Value!;
        Assert.Equal(UserId, merged.UserId);
        Assert.Equal(2, merged.Items.Count);
        Assert.Equal(2, QuantityOf(merged, VariantA));
        Assert.Equal(4, QuantityOf(merged, VariantB)); // 3 + 1 → Phase 3 duplicate rule.
        Assert.Equal(4, merged.Version);

        // The merged target is persisted AND the Guest source is consumed in one atomic call.
        store.Verify(
            s => s.MergeAndConsumeAsync(
                It.Is<CartState>(c =>
                    c.UserId == UserId &&
                    c.Version == 4 &&
                    QuantityOf(c, VariantA) == 2 &&
                    QuantityOf(c, VariantB) == 4),
                It.Is<long>(expected => expected == 3),
                It.Is<long>(expectedSource => expectedSource == 1),
                It.Is<CartId>(id => id.Value == GuestCartId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.DeleteAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingTargetCart_CreatesAuthenticatedCart_ThenMergesGuestItems()
    {
        var store = CreateStoreMock(
            source: GuestCart(Item(VariantA, 2)),
            existingTarget: null,
            createdTarget: EmptyAuthenticatedCart(version: 1));
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var merged = result.Value!;
        Assert.Equal(UserId, merged.UserId);
        Assert.Single(merged.Items);
        Assert.Equal(2, QuantityOf(merged, VariantA));
        Assert.Equal(2, merged.Version);

        store.Verify(
            s => s.CreateForUserAsync(
                It.Is<CartState>(c => c.UserId == UserId && c.Version == 1 && c.Items.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(
            s => s.MergeAndConsumeAsync(
                It.Is<CartState>(c => c.UserId == UserId && c.Version == 2 && QuantityOf(c, VariantA) == 2),
                It.Is<long>(expected => expected == 1),
                It.Is<long>(expectedSource => expectedSource == 1),
                It.Is<CartId>(id => id.Value == GuestCartId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired_AndDoesNothing()
    {
        var mock = new Mock<ICartStore>();
        var handler = new MergeCartCommandHandler(mock.Object, FixedDateTimeProvider.CreateDefault(), Anonymous());

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
        Assert.Equal("Cart.AuthenticationRequired", result.Error.Code);
        mock.Verify(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(s => s.MergeAndConsumeAsync(
            It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MissingGuestCart_ReturnsCartNotFound_AndDoesNotMutateTarget()
    {
        var existingTarget = AuthenticatedCart(userId: null, version: 3, Item(VariantB, 3));
        var store = CreateStoreMock(source: null, existingTarget: existingTarget);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        store.Verify(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.MergeAndConsumeAsync(
            It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AlreadyConsumedGuestCart_ReturnsCartNotFound_AndDoesNotMerge()
    {
        // A retry of a merge whose Guest source was already consumed resolves the source as
        // missing and must never re-apply the Guest quantities (logical idempotency).
        var existingTarget = AuthenticatedCart(userId: null, version: 3, Item(VariantB, 3));
        var store = CreateStoreMock(source: null, existingTarget: existingTarget);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Cart.CartNotFound", result.Error.Code);
        store.Verify(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.MergeAndConsumeAsync(
            It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InvalidGuestCartId_ReturnsInvalidCartId_AndDoesNothing()
    {
        var mock = new Mock<ICartStore>();
        var handler = new MergeCartCommandHandler(mock.Object, FixedDateTimeProvider.CreateDefault(), Authenticated());

        var result = await handler.HandleAsync(new MergeCartCommand(Guid.Empty), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Cart.InvalidCartId", result.Error.Code);
        mock.Verify(s => s.GetAsync(It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(s => s.MergeAndConsumeAsync(
            It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AuthenticatedCartAsSource_ReturnsAccessDenied_AndDoesNotMutateTarget()
    {
        // A Cart that is owned by a user is never a valid Guest merge source.
        var authenticatedSource = AuthenticatedCart(userId: Guid.NewGuid(), version: 2, Item(VariantA, 2));
        var existingTarget = AuthenticatedCart(userId: null, version: 3, Item(VariantB, 3));
        var store = CreateStoreMock(source: authenticatedSource, existingTarget: existingTarget);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error!.Code);
        store.Verify(s => s.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.MergeAndConsumeAsync(
            It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExistingTargetOwnershipMismatch_ReturnsAccessDenied()
    {
        var store = CreateStoreMock(
            source: GuestCart(Item(VariantA, 2)),
            existingTarget: AuthenticatedCart(userId: Guid.NewGuid(), version: 3, Item(VariantB, 3)));
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Cart.CartAccessDenied", result.Error!.Code);
        store.Verify(s => s.MergeAndConsumeAsync(
            It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmptyGuestCart_ReturnsTargetUnchanged_AndConsumesEmptyGuestCart()
    {
        var existingTarget = AuthenticatedCart(userId: null, version: 3, Item(VariantB, 3));
        var store = CreateStoreMock(source: GuestCart(), existingTarget: existingTarget);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var merged = result.Value!;
        Assert.Equal(UserId, merged.UserId);
        Assert.Equal(3, merged.Version);
        Assert.Single(merged.Items);
        Assert.Equal(3, QuantityOf(merged, VariantB));
        store.Verify(
            s => s.MergeAndConsumeAsync(
                It.Is<CartState>(c => c.Version == 3 && QuantityOf(c, VariantB) == 3),
                It.Is<long>(expected => expected == 3),
                It.Is<long>(expectedSource => expectedSource == 1),
                It.Is<CartId>(id => id.Value == GuestCartId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_QuantityOverflow_ReturnsQuantityLimitExceeded_AndDoesNotPersistOrConsume()
    {
        // The Domain Merge invariant is an int.MaxValue overflow on the summed quantity.
        var store = CreateStoreMock(
            source: GuestCart(Item(VariantA, 2)),
            existingTarget: AuthenticatedCart(userId: null, version: 3, Item(VariantA, int.MaxValue - 1)));
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Cart.QuantityLimitExceeded", result.Error.Code);
        store.Verify(s => s.MergeAndConsumeAsync(
            It.IsAny<CartState>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CartId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MergeVersionConflict_ReturnsConcurrencyConflict()
    {
        var store = CreateStoreMock(
            source: GuestCart(Item(VariantA, 2)),
            existingTarget: AuthenticatedCart(userId: null, version: 3, Item(VariantB, 3)),
            saveStatus: CartSaveStatus.VersionConflict);
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_CreateDuplicateActiveCart_ReReadsWinner_AndMergesIntoIt()
    {
        var winner = AuthenticatedCart(userId: null, version: 2, Item(VariantB, 1));
        var mock = new Mock<ICartStore>();
        mock
            .Setup(s => s.GetAsync(new CartId(GuestCartId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuestCart(Item(VariantA, 2)));
        mock
            .SetupSequence(s => s.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CartState?)null) // First read: no active cart yet.
            .ReturnsAsync(winner); // Winner re-read after DuplicateActiveCart.
        mock
            .Setup(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.DuplicateActiveCart, null));
        mock
            .Setup(s => s.MergeAndConsumeAsync(
                It.IsAny<CartState>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CartId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.Success, null));
        var handler = CreateHandler(mock);

        var result = await handler.HandleAsync(new MergeCartCommand(GuestCartId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var merged = result.Value!;
        Assert.Equal(UserId, merged.UserId);
        Assert.Equal(2, merged.Items.Count);
        Assert.Equal(2, QuantityOf(merged, VariantA));
        Assert.Equal(1, QuantityOf(merged, VariantB));
        Assert.Equal(3, merged.Version);
        mock.Verify(
            s => s.MergeAndConsumeAsync(
                It.Is<CartState>(c => c.Version == 3 && QuantityOf(c, VariantA) == 2 && QuantityOf(c, VariantB) == 1),
                It.Is<long>(expected => expected == 2),
                It.Is<long>(expectedSource => expectedSource == 1),
                It.Is<CartId>(id => id.Value == GuestCartId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static MergeCartCommandHandler CreateHandler(Mock<ICartStore> store) =>
        new(store.Object, FixedDateTimeProvider.CreateDefault(), Authenticated());

    private static FakeCurrentUser Authenticated() =>
        new() { UserId = UserId, IsAuthenticated = true };

    private static FakeCurrentUser Anonymous() =>
        new() { UserId = null, IsAuthenticated = false };

    private static Mock<ICartStore> CreateStoreMock(
        CartState? source = null,
        CartState? existingTarget = null,
        CartState? createdTarget = null,
        CartSaveStatus saveStatus = CartSaveStatus.Success,
        CartState? saved = null)
    {
        var mock = new Mock<ICartStore>();
        mock
            .Setup(s => s.GetAsync(new CartId(GuestCartId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        mock
            .Setup(s => s.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTarget);
        mock
            .Setup(s => s.CreateForUserAsync(It.IsAny<CartState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(CartSaveStatus.Success, createdTarget));
        mock
            .Setup(s => s.MergeAndConsumeAsync(
                It.IsAny<CartState>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CartId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartSaveResult(saveStatus, saved));
        return mock;
    }

    private static CartState GuestCart(params CartItemState[] items) =>
        new(
            new CartId(GuestCartId),
            null,
            1,
            RedisCartTestData.FixedTime,
            RedisCartTestData.FixedTime.AddMinutes(1),
            items);

    private static CartState AuthenticatedCart(Guid? userId = null, long version = 1, params CartItemState[] items) =>
        new(
            new CartId(Guid.NewGuid()),
            userId ?? UserId,
            version,
            RedisCartTestData.FixedTime,
            RedisCartTestData.FixedTime.AddMinutes(2),
            items);

    private static CartState EmptyAuthenticatedCart(long version = 1) =>
        new(
            new CartId(Guid.NewGuid()),
            UserId,
            version,
            RedisCartTestData.FixedTime,
            RedisCartTestData.FixedTime.AddMinutes(2),
            []);

    private static CartItemState Item(Guid variantId, int quantity) =>
        new(
            new ProductId(Guid.NewGuid()),
            new VariantId(variantId),
            new Sku("SKU-1001"),
            "Wireless Mouse",
            new Money(24.99m, "USD"),
            quantity);

    private static int QuantityOf(CartState cart, Guid variantId) =>
        cart.Items.Single(i => i.VariantId == new VariantId(variantId)).Quantity;
}
