using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Orders.Application.Commands.Checkout;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

/// <summary>
/// Phase 4.7/4.8 Checkout orchestration coverage. The handler is exercised against in-memory fakes for
/// the Cart checkout snapshot, the Cart consumption contract, the Catalog revalidation lookup and the
/// Checkout store so every branch (authentication, idempotency, ownership, availability, atomic
/// persistence and the best-effort post-commit Cart clear) is deterministic.
/// </summary>
public class CheckoutCommandHandlerTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Unauthenticated_ReturnsAuthenticationRequired()
    {
        var carts = new FakeCartCheckoutSnapshotProvider();
        var lookup = new FakeCatalogVariantCheckoutLookup([]);
        var store = new FakeCheckoutStore();
        var handler = CreateHandler(userId: null, carts, lookup, store);

        var result = await handler.HandleAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal("Order.AuthenticationRequired", result.Error.Code);
        Assert.Equal(0, carts.Calls);
        Assert.Equal(0, lookup.Calls);
        Assert.Equal(0, store.PersistCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_InvalidIdempotencyKey_ReturnsValidationError(string? key)
    {
        var carts = new FakeCartCheckoutSnapshotProvider();
        var store = new FakeCheckoutStore();
        var handler = CreateHandler(Guid.NewGuid(), carts, new FakeCatalogVariantCheckoutLookup([]), store);

        var result = await handler.HandleAsync(
            CreateCommand(Guid.NewGuid(), key!),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Order.InvalidIdempotencyKey", result.Error.Code);
        Assert.Equal(0, carts.Calls);
        Assert.Equal(0, store.PersistCalls);
    }

    [Fact]
    public async Task HandleAsync_OverlongIdempotencyKey_ReturnsValidationError()
    {
        var handler = CreateHandler(
            Guid.NewGuid(),
            new FakeCartCheckoutSnapshotProvider(),
            new FakeCatalogVariantCheckoutLookup([]),
            new FakeCheckoutStore());

        var result = await handler.HandleAsync(
            CreateCommand(Guid.NewGuid(), new string('k', 129)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Order.InvalidIdempotencyKey", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_MissingCart_ReturnsCartNotFound()
    {
        var carts = new FakeCartCheckoutSnapshotProvider();
        var store = new FakeCheckoutStore();
        var handler = CreateHandler(Guid.NewGuid(), carts, new FakeCatalogVariantCheckoutLookup([]), store);

        var result = await handler.HandleAsync(CreateCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Order.CartNotFound", result.Error.Code);
        Assert.Equal(0, store.PersistCalls);
    }

    [Fact]
    public async Task HandleAsync_AnotherUsersCart_ReturnsCartAccessDenied()
    {
        var cartId = Guid.NewGuid();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, customerId: Guid.NewGuid(), CreateItem());
        var store = new FakeCheckoutStore();
        var handler = CreateHandler(Guid.NewGuid(), carts, new FakeCatalogVariantCheckoutLookup([]), store);

        var result = await handler.HandleAsync(CreateCommand(cartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Equal("Order.CartAccessDenied", result.Error.Code);
        Assert.Equal(0, store.PersistCalls);
    }

    [Fact]
    public async Task HandleAsync_EmptyCart_ReturnsCartEmpty()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId);
        var store = new FakeCheckoutStore();
        var handler = CreateHandler(userId, carts, new FakeCatalogVariantCheckoutLookup([]), store);

        var result = await handler.HandleAsync(CreateCommand(cartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Order.CartEmpty", result.Error.Code);
        Assert.Equal(0, store.PersistCalls);
    }

    [Fact]
    public async Task HandleAsync_VariantNoLongerAvailable_ReturnsVariantUnavailable()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var store = new FakeCheckoutStore();
        var handler = CreateHandler(userId, carts, new FakeCatalogVariantCheckoutLookup([]), store);

        var result = await handler.HandleAsync(CreateCommand(cartId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Order.VariantUnavailable", result.Error.Code);
        Assert.Equal(0, store.PersistCalls);
    }

    [Fact]
    public async Task HandleAsync_InvalidAddress_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var lookup = FakeCatalogVariantCheckoutLookup.Available(item);
        var store = new FakeCheckoutStore();
        var handler = CreateHandler(userId, carts, lookup, store);

        var command = CreateCommand(cartId) with
        {
            ShippingAddress = new CheckoutAddressData("", "", null, "London", "Region", "SW1A", "GB"),
        };

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("Order.InvalidAddress", result.Error.Code);
        Assert.Equal(0, store.PersistCalls);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_PersistsOrderWithCurrentCatalogPrice()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem(quantity: 2, cartUnitPrice: 5.00m, variantId: Guid.NewGuid());
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var lookup = FakeCatalogVariantCheckoutLookup.Available(item, currentPrice: 19.50m);
        var store = new FakeCheckoutStore();
        var handler = CreateHandler(userId, carts, lookup, store);

        var result = await handler.HandleAsync(CreateCommand(cartId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = result.Value!;
        var line = Assert.Single(order.LineItems);
        Assert.Equal(19.50m, line.UnitPrice.Amount);
        Assert.Equal("USD", line.UnitPrice.Currency);

        Assert.Equal(1, store.PersistCalls);
        Assert.Equal(1, lookup.Calls);
        Assert.NotNull(store.LastPersisted);
        Assert.Equal(order.Id.Value, store.LastPersisted!.OrderId);
        Assert.Equal(userId, store.LastPersisted.UserId);
        Assert.NotEmpty(store.LastPersisted.RequestFingerprint);
    }

    [Fact]
    public async Task HandleAsync_SafeRetry_ReturnsOriginalOrder_WithoutNewPersist()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var command = CreateCommand(cartId);
        var fingerprint = CheckoutRequestFingerprint.Compute(cartId, command.ShippingAddress, command.BillingAddress);

        var original = Order.Place(
            OrderId.Create(),
            new CustomerId(userId),
            OrderNumber.Create(),
            ShippingAddress(),
            ShippingAddress(),
            [new OrderLineItem(
                OrderProductId.Create(),
                OrderVariantId.Create(),
                new OrderSku("SKU-1"),
                "Product",
                new OrderMoney(10m, "USD"),
                1)],
            UtcNow);

        var store = new FakeCheckoutStore();
        store.SeedRetry(userId, command.IdempotencyKey, fingerprint, original);
        var carts = new FakeCartCheckoutSnapshotProvider();
        var consume = new FakeCartCheckoutConsumption();
        var handler = CreateHandler(userId, carts, new FakeCatalogVariantCheckoutLookup([]), store, consume);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(original.Id, result.Value!.Id);
        Assert.Equal(0, carts.Calls);
        Assert.Equal(0, store.PersistCalls);
        Assert.Equal(0, consume.Calls);
    }

    [Fact]
    public async Task HandleAsync_SameKeyDifferentRequest_ReturnsIdempotencyConflict()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var command = CreateCommand(cartId);
        var differentFingerprint = CheckoutRequestFingerprint.Compute(
            cartId,
            new CheckoutAddressData("Ada", "1 Way", null, "Paris", "Paris", "75001", "FR"),
            new CheckoutAddressData("Ada", "1 Way", null, "Paris", "Paris", "75001", "FR"));

        var original = Order.Place(
            OrderId.Create(),
            new CustomerId(userId),
            OrderNumber.Create(),
            ShippingAddress(),
            ShippingAddress(),
            [CreateOrderLineItem()],
            UtcNow);

        var store = new FakeCheckoutStore();
        store.SeedRetry(userId, command.IdempotencyKey, differentFingerprint, original);
        var handler = CreateHandler(userId, new FakeCartCheckoutSnapshotProvider(), new FakeCatalogVariantCheckoutLookup([]), store);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("Order.IdempotencyConflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_ConcurrentDuplicatePersist_ResolvesToWinningOrder()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var lookup = FakeCatalogVariantCheckoutLookup.Available(item);
        var command = CreateCommand(cartId);

        var fingerprint = CheckoutRequestFingerprint.Compute(cartId, command.ShippingAddress, command.BillingAddress);
        var winningOrder = Order.Place(
            OrderId.Create(),
            new CustomerId(userId),
            OrderNumber.Create(),
            ShippingAddress(),
            ShippingAddress(),
            [CreateOrderLineItem()],
            UtcNow);

        var store = new FakeCheckoutStore();
        store.SimulateDuplicate(userId, command.IdempotencyKey, fingerprint, winningOrder);
        var consume = new FakeCartCheckoutConsumption();
        var handler = CreateHandler(userId, carts, lookup, store, consume);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(winningOrder.Id, result.Value!.Id);
        Assert.Equal(1, store.PersistCalls);
        Assert.Equal(0, consume.Calls);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ConsumesCartOnlyAfterPersistCommit()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var lookup = FakeCatalogVariantCheckoutLookup.Available(item);
        var store = new FakeCheckoutStore();
        var consume = new FakeCartCheckoutConsumption();
        var handler = CreateHandler(userId, carts, lookup, store, consume);

        var result = await handler.HandleAsync(CreateCommand(cartId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.PersistCalls);
        Assert.Equal(1, consume.Calls);
        Assert.Equal(cartId, consume.LastCartId);
        Assert.Equal(userId, consume.LastCustomerId);
    }

    [Fact]
    public async Task HandleAsync_PersistFailure_ThrowsBeforeCartConsumption()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var lookup = FakeCatalogVariantCheckoutLookup.Available(item);
        var store = new FakeCheckoutStore();
        store.SimulatePersistFailure();
        var consume = new FakeCartCheckoutConsumption();
        var handler = CreateHandler(userId, carts, lookup, store, consume);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(CreateCommand(cartId), CancellationToken.None));

        Assert.Equal("Simulated Checkout persist failure.", exception.Message);
        Assert.Equal(1, store.PersistCalls);
        Assert.Equal(0, consume.Calls);
    }

    [Fact]
    public void Fingerprint_IsStableForTheSameRequest_AndSensitiveToAddressChanges()
    {
        var cartId = Guid.NewGuid();
        var shipping = new CheckoutAddressData("Ada", "1 Way", null, "London", "London", "SW1A 1AA", "GB");
        var billing = new CheckoutAddressData("Ada", "1 Way", null, "London", "London", "SW1A 1AA", "GB");

        var first = CheckoutRequestFingerprint.Compute(cartId, shipping, billing);
        var retry = CheckoutRequestFingerprint.Compute(cartId, shipping, billing);

        Assert.Equal(first, retry);

        var different = CheckoutRequestFingerprint.Compute(
            cartId,
            shipping,
            billing with { City = "Manchester" });

        Assert.NotEqual(first, different);
    }

    [Fact]
    public async Task HandleAsync_CartConsumptionFailure_OrderStillSucceeds()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var lookup = FakeCatalogVariantCheckoutLookup.Available(item);
        var store = new FakeCheckoutStore();
        var consume = new FakeCartCheckoutConsumption(CartCheckoutConsumptionStatus.Failed);
        var handler = CreateHandler(userId, carts, lookup, store, consume);

        var result = await handler.HandleAsync(CreateCommand(cartId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.PersistCalls);
        Assert.Equal(1, consume.Calls);
    }

    [Fact]
    public async Task HandleAsync_CartConsumptionThrow_OrderStillSucceeds()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var lookup = FakeCatalogVariantCheckoutLookup.Available(item);
        var store = new FakeCheckoutStore();
        var consume = new FakeCartCheckoutConsumption(throwOnConsume: true);
        var handler = CreateHandler(userId, carts, lookup, store, consume);

        var result = await handler.HandleAsync(CreateCommand(cartId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.PersistCalls);
        Assert.Equal(1, consume.Calls);
    }

    [Fact]
    public async Task HandleAsync_RetryAfterCartConsumptionFailure_ReturnsSameOrderWithoutSecondConsume()
    {
        var userId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var item = CreateItem();
        var carts = new FakeCartCheckoutSnapshotProvider();
        carts.Seed(cartId, userId, item);
        var lookup = FakeCatalogVariantCheckoutLookup.Available(item);
        var store = new FakeCheckoutStore();
        var consume = new FakeCartCheckoutConsumption(CartCheckoutConsumptionStatus.Failed);
        var handler = CreateHandler(userId, carts, lookup, store, consume);

        var command = CreateCommand(cartId);

        var first = await handler.HandleAsync(command, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, consume.Calls);

        var retry = await handler.HandleAsync(command, CancellationToken.None);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value!.Id, retry.Value!.Id);
        Assert.Equal(1, store.PersistCalls);
        Assert.Equal(1, consume.Calls);
    }

    private static CheckoutCommandHandler CreateHandler(
        Guid? userId,
        FakeCartCheckoutSnapshotProvider carts,
        FakeCatalogVariantCheckoutLookup lookup,
        FakeCheckoutStore store) =>
        CreateHandler(userId, carts, lookup, store, new FakeCartCheckoutConsumption());

    private static CheckoutCommandHandler CreateHandler(
        Guid? userId,
        FakeCartCheckoutSnapshotProvider carts,
        FakeCatalogVariantCheckoutLookup lookup,
        FakeCheckoutStore store,
        FakeCartCheckoutConsumption consumption) =>
        new(
            new FakeCurrentUser { UserId = userId, IsAuthenticated = userId is not null },
            FixedDateTimeProvider.CreateDefault(),
            carts,
            lookup,
            new CheckoutOrderSnapshotBuilder(),
            store,
            consumption);

    private static CheckoutCommand CreateCommand(Guid cartId, string key = "checkout-key-1") =>
        new(cartId, key, ValidAddress(), ValidAddress());

    private static CheckoutAddressData ValidAddress() =>
        new("Ada Lovelace", "1 Analytical Engine Way", null, "London", "Greater London", "SW1A 1AA", "GB");

    private static OrderAddress ShippingAddress() =>
        new("Ada Lovelace", "1 Analytical Engine Way", null, "London", "Greater London", "SW1A 1AA", "GB");

    private static CartCheckoutItemSnapshot CreateItem(
        int quantity = 1,
        decimal cartUnitPrice = 5m,
        Guid? variantId = null) =>
        new(
            Guid.NewGuid(),
            variantId ?? Guid.NewGuid(),
            "SKU-1",
            quantity,
            cartUnitPrice,
            "USD");

    private static OrderLineItem CreateOrderLineItem() =>
        new(
            OrderProductId.Create(),
            OrderVariantId.Create(),
            new OrderSku("SKU-1"),
            "Product",
            new OrderMoney(10m, "USD"),
            1);

    private sealed class FakeCartCheckoutSnapshotProvider : ICartCheckoutSnapshotProvider
    {
        private readonly Dictionary<Guid, CartCheckoutSnapshot> _carts = new();

        public int Calls { get; private set; }

        public void Seed(Guid cartId, Guid? customerId, params CartCheckoutItemSnapshot[] items) =>
            _carts[cartId] = new CartCheckoutSnapshot(cartId, customerId, items.ToList());

        public Task<CartCheckoutSnapshot?> GetCheckoutSnapshotAsync(
            Guid cartId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            _ = _carts.TryGetValue(cartId, out var snapshot);
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeCartCheckoutConsumption : ICartCheckoutConsumption
    {
        private readonly CartCheckoutConsumptionStatus? _fixedStatus;
        private readonly bool _throwOnConsume;

        public FakeCartCheckoutConsumption(CartCheckoutConsumptionStatus? status = CartCheckoutConsumptionStatus.Consumed)
        {
            _fixedStatus = status;
        }

        public FakeCartCheckoutConsumption(bool throwOnConsume)
        {
            _throwOnConsume = throwOnConsume;
        }

        public int Calls { get; private set; }

        public Guid? LastCartId { get; private set; }

        public Guid? LastCustomerId { get; private set; }

        public Task<CartCheckoutConsumptionResult> ConsumeAsync(
            Guid cartId,
            Guid customerId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastCartId = cartId;
            LastCustomerId = customerId;

            if (_throwOnConsume)
                throw new InvalidOperationException("Simulated Cart consumption failure.");

            return Task.FromResult(new CartCheckoutConsumptionResult(_fixedStatus ?? CartCheckoutConsumptionStatus.Consumed));
        }
    }

    private sealed class FakeCatalogVariantCheckoutLookup : ICatalogVariantCheckoutLookup
    {
        private readonly IReadOnlyCollection<CatalogVariantCheckoutInfo> _results;

        public FakeCatalogVariantCheckoutLookup(IReadOnlyCollection<CatalogVariantCheckoutInfo> results)
        {
            _results = results;
        }

        public int Calls { get; private set; }

        public static FakeCatalogVariantCheckoutLookup Available(
            CartCheckoutItemSnapshot item,
            decimal currentPrice = 10m) =>
            new(
            [
                new CatalogVariantCheckoutInfo(
                    item.VariantId,
                    CatalogVariantEligibilityStatus.Available,
                    item.ProductId,
                    item.Sku,
                    "Wireless Mouse",
                    currentPrice,
                    "USD"),
            ]);

        public Task<IReadOnlyCollection<CatalogVariantCheckoutInfo>> GetCheckoutInfoAsync(
            IReadOnlyCollection<Guid> variantIds,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_results);
        }
    }

    private sealed class FakeCheckoutStore : ICheckoutStore
    {
        private readonly List<CheckoutIdempotencyRecord> _records = [];
        private readonly Dictionary<Guid, Order> _orders = new();

        public int PersistCalls { get; private set; }

        public CheckoutIdempotencyRecord? LastPersisted { get; private set; }

        public void SimulatePersistFailure() => _throwOnPersist = true;

        private bool _throwOnPersist;

        public void SeedRetry(Guid userId, string key, string fingerprint, Order order)
        {
            _records.Add(new CheckoutIdempotencyRecord(Guid.NewGuid(), userId, key, order.Id.Value, fingerprint, UtcNow));
            _orders[order.Id.Value] = order;
        }

        public void SimulateDuplicate(Guid userId, string key, string fingerprint, Order order)
        {
            _orders[order.Id.Value] = order;
            _pendingDuplicate = new CheckoutIdempotencyRecord(Guid.NewGuid(), userId, key, order.Id.Value, fingerprint, UtcNow);
        }

        private CheckoutIdempotencyRecord? _pendingDuplicate;

        public Task<CheckoutIdempotencyRecord?> FindIdempotencyRecordAsync(
            Guid userId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _records.FirstOrDefault(
                    record => record.UserId == userId && record.IdempotencyKey == idempotencyKey));

        public Task<Order?> GetOrderAsync(OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.TryGetValue(orderId.Value, out var order) ? order : null);

        public Task<CheckoutPersistResult> PersistAsync(
            Order order,
            CheckoutIdempotencyRecord idempotency,
            CancellationToken cancellationToken = default)
        {
            PersistCalls++;
            LastPersisted = idempotency;

            if (_throwOnPersist)
                throw new InvalidOperationException("Simulated Checkout persist failure.");

            if (_pendingDuplicate is not null
                && _pendingDuplicate.UserId == idempotency.UserId
                && _pendingDuplicate.IdempotencyKey == idempotency.IdempotencyKey)
            {
                var duplicate = _pendingDuplicate;
                _pendingDuplicate = null;
                return Task.FromResult(new CheckoutPersistResult(CheckoutPersistStatus.Duplicate, duplicate));
            }

            _records.Add(idempotency);
            _orders[order.Id.Value] = order;
            return Task.FromResult(new CheckoutPersistResult(CheckoutPersistStatus.Created, null));
        }
    }
}
