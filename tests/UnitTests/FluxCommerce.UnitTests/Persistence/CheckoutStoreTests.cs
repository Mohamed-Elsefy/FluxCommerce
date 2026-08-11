using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 4.7 persistence coverage for <see cref="CheckoutStore"/>: the Order and its idempotency
/// record are written atomically and a duplicate (Customer, Idempotency-Key) is surfaced as a
/// <see cref="CheckoutPersistStatus.Duplicate"/> result instead of a second order.
/// </summary>
public class CheckoutStoreTests : IDisposable
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 10, 8, 15, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public CheckoutStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task PersistAsync_WritesOrderAndIdempotency_Atomically()
    {
        await using var context = CreateContext();
        var store = new CheckoutStore(context);

        var userId = Guid.NewGuid();
        var orderId = OrderId.Create();
        var order = CreateOrder(orderId, userId);
        var record = CreateRecord(userId, orderId, "checkout-key-1", "fingerprint-1");

        var result = await store.PersistAsync(order, record, CancellationToken.None);

        Assert.Equal(CheckoutPersistStatus.Created, result.Status);

        var loaded = await store.FindIdempotencyRecordAsync(userId, "checkout-key-1", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(record.Id, loaded!.Id);
        Assert.Equal(orderId.Value, loaded.OrderId);
        Assert.Equal("fingerprint-1", loaded.RequestFingerprint);

        var storedOrder = await store.GetOrderAsync(orderId, CancellationToken.None);
        Assert.NotNull(storedOrder);
        Assert.Equal(orderId, storedOrder!.Id);
        Assert.Single(storedOrder.LineItems);
    }

    [Fact]
    public async Task PersistAsync_DuplicateCustomerAndKey_ReturnsDuplicate_WithoutSecondOrder()
    {
        await using var context = CreateContext();
        var store = new CheckoutStore(context);
        var userId = Guid.NewGuid();

        var firstOrderId = OrderId.Create();
        await store.PersistAsync(
            CreateOrder(firstOrderId, userId),
            CreateRecord(userId, firstOrderId, "checkout-key", "fingerprint-1"),
            CancellationToken.None);

        var secondOrderId = OrderId.Create();
        var second = await store.PersistAsync(
            CreateOrder(secondOrderId, userId),
            CreateRecord(userId, secondOrderId, "checkout-key", "fingerprint-2"),
            CancellationToken.None);

        Assert.Equal(CheckoutPersistStatus.Duplicate, second.Status);
        Assert.NotNull(second.Existing);
        Assert.Equal(firstOrderId.Value, second.Existing!.OrderId);
        Assert.Equal("fingerprint-1", second.Existing.RequestFingerprint);

        var orderCount = await context.Orders.CountAsync();
        Assert.Equal(1, orderCount);
        Assert.Null(await context.Orders.FirstOrDefaultAsync(o => o.Id == secondOrderId));
    }

    [Fact]
    public async Task FindIdempotencyRecordAsync_ReturnsNull_WhenMissing()
    {
        await using var context = CreateContext();
        var store = new CheckoutStore(context);

        var loaded = await store.FindIdempotencyRecordAsync(Guid.NewGuid(), "missing-key", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetOrderAsync_ReturnsNull_WhenMissing()
    {
        await using var context = CreateContext();
        var store = new CheckoutStore(context);

        var order = await store.GetOrderAsync(OrderId.Create(), CancellationToken.None);

        Assert.Null(order);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private OrdersDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new OrdersDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        context.Database.EnsureCreated();

        return context;
    }

    private static Order CreateOrder(OrderId orderId, Guid userId) =>
        Order.Place(
            orderId,
            new CustomerId(userId),
            OrderNumber.Create(),
            CreateAddress(),
            CreateAddress(),
            [
                new OrderLineItem(
                    OrderProductId.Create(),
                    OrderVariantId.Create(),
                    new OrderSku("SKU-1"),
                    "Test Product",
                    new OrderMoney(10m, "USD"),
                    1),
            ],
            CreatedAtUtc);

    private static CheckoutIdempotencyRecord CreateRecord(Guid userId, OrderId orderId, string key, string fingerprint) =>
        new(Guid.NewGuid(), userId, key, orderId.Value, fingerprint, CreatedAtUtc);

    private static OrderAddress CreateAddress() =>
        new("Shipping Recipient", "Shipping Line 1", null, "Shipping City", "Region", "1000", "US");

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}