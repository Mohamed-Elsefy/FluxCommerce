using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

public class OrdersPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public OrdersPersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task Order_RoundTrips_WithAllProperties()
    {
        await using var context = CreateContext();

        var createdAtUtc = new DateTimeOffset(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);
        var orderId = OrderId.Create();
        var customerId = CustomerId.Create();

        var order = Order.Place(
            orderId,
            customerId,
            new OrderNumber("ORD-001"),
            OrderAddressFields("Shipping"),
            OrderAddressFields("Billing"),
            [CreateItem("ABC-1"), CreateItem("ABC-2")],
            createdAtUtc);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var loaded = await context.Orders
            .Include(o => o.LineItems)
            .SingleAsync(o => o.Id == orderId);

        Assert.Equal(orderId, loaded.Id);
        Assert.Equal(customerId, loaded.CustomerId);
        Assert.Equal(new OrderNumber("ORD-001"), loaded.OrderNumber);
        Assert.Equal(OrderStatus.Pending, loaded.Status);
        Assert.Equal(order.Version, loaded.Version);
        Assert.Equal(createdAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(createdAtUtc, loaded.UpdatedAtUtc);
        Assert.Equal(OrderAddressFields("Shipping"), loaded.ShippingAddress);
        Assert.Equal(OrderAddressFields("Billing"), loaded.BillingAddress);
        Assert.Equal(OrderMoney.Zero("USD"), loaded.ShippingCost);
        Assert.Equal(OrderMoney.Zero("USD"), loaded.Discount);
        Assert.Equal(2, loaded.LineItems.Count);
        Assert.Equal(10m, loaded.Subtotal.Amount);
    }

    [Fact]
    public async Task Order_LineItems_RoundTrip_InOrder()
    {
        await using var context = CreateContext();

        var createdAtUtc = new DateTimeOffset(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);
        var orderId = OrderId.Create();
        var variantId = OrderVariantId.Create();

        var order = Order.Place(
            orderId,
            CustomerId.Create(),
            new OrderNumber("ORD-002"),
            CreateAddress(),
            CreateAddress(),
            [new OrderLineItem(
                OrderProductId.Create(),
                variantId,
                new OrderSku("SKU-X"),
                "Test Product",
                new OrderMoney(25.5m, "USD"),
                3)],
            createdAtUtc);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var loaded = await context.Orders
            .Include(o => o.LineItems)
            .SingleAsync(o => o.Id == orderId);

        var item = Assert.Single(loaded.LineItems);
        Assert.Equal(variantId, item.VariantId);
        Assert.Equal(new OrderSku("SKU-X"), item.Sku);
        Assert.Equal("Test Product", item.ProductName);
        Assert.Equal(new OrderMoney(25.5m, "USD"), item.UnitPrice);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(76.5m, item.LineTotal.Amount);
    }

    [Fact]
    public async Task TwoOrders_CanReferenceSameVariant()
    {
        await using var context = CreateContext();

        var createdAtUtc = new DateTimeOffset(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);
        var sharedVariant = OrderVariantId.Create();
        var sharedProduct = OrderProductId.Create();

        var first = Order.Place(
            OrderId.Create(),
            CustomerId.Create(),
            new OrderNumber("ORD-003"),
            CreateAddress(),
            CreateAddress(),
            [new OrderLineItem(sharedProduct, sharedVariant, new OrderSku("SKU-Y"), "Product", new OrderMoney(5m, "USD"), 1)],
            createdAtUtc);

        var second = Order.Place(
            OrderId.Create(),
            CustomerId.Create(),
            new OrderNumber("ORD-004"),
            CreateAddress(),
            CreateAddress(),
            [new OrderLineItem(sharedProduct, sharedVariant, new OrderSku("SKU-Y"), "Product", new OrderMoney(5m, "USD"), 1)],
            createdAtUtc);

        context.Orders.AddRange(first, second);
        await context.SaveChangesAsync();

        var orders = await context.Orders
            .Include(o => o.LineItems)
            .OrderBy(o => o.OrderNumber.Value)
            .ToListAsync();

        Assert.Equal(2, orders.Count);
        Assert.All(orders, order => Assert.Single(order.LineItems));
    }

    [Fact]
    public async Task CheckoutIdempotency_Persists()
    {
        await using var context = CreateContext();

        var record = CheckoutIdempotency.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "checkout-key-1",
            Guid.NewGuid(),
            "fingerprint-1",
            new DateTimeOffset(2026, 2, 10, 8, 15, 0, TimeSpan.Zero));

        context.CheckoutIdempotencies.Add(record);
        await context.SaveChangesAsync();

        var loaded = await context.CheckoutIdempotencies.SingleAsync(e => e.Id == record.Id);

        Assert.Equal(record.UserId, loaded.UserId);
        Assert.Equal("checkout-key-1", loaded.IdempotencyKey);
        Assert.Equal(record.OrderId, loaded.OrderId);
        Assert.Equal("fingerprint-1", loaded.RequestFingerprint);
        Assert.Equal(record.CreatedAtUtc, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task CheckoutIdempotency_RejectsDuplicateUserIdAndKey()
    {
        await using var context = CreateContext();

        var userId = Guid.NewGuid();

        context.CheckoutIdempotencies.Add(CheckoutIdempotency.Create(
            Guid.NewGuid(),
            userId,
            "checkout-key",
            Guid.NewGuid(),
            "fingerprint-1",
            new DateTimeOffset(2026, 2, 10, 8, 15, 0, TimeSpan.Zero)));

        context.CheckoutIdempotencies.Add(CheckoutIdempotency.Create(
            Guid.NewGuid(),
            userId,
            "checkout-key",
            Guid.NewGuid(),
            "fingerprint-2",
            new DateTimeOffset(2026, 2, 10, 8, 15, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Order_RejectsDuplicateOrderNumber()
    {
        await using var context = CreateContext();

        var createdAtUtc = new DateTimeOffset(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

        context.Orders.Add(Order.Place(
            OrderId.Create(),
            CustomerId.Create(),
            new OrderNumber("ORD-DUP"),
            CreateAddress(),
            CreateAddress(),
            [CreateItem("ABC-1")],
            createdAtUtc));

        context.Orders.Add(Order.Place(
            OrderId.Create(),
            CustomerId.Create(),
            new OrderNumber("ORD-DUP"),
            CreateAddress(),
            CreateAddress(),
            [CreateItem("ABC-2")],
            createdAtUtc));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
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

    private static OrderAddress CreateAddress() =>
        OrderAddressFields("Shipping");    private static OrderAddress OrderAddressFields(string prefix) =>
        new($"{prefix} Recipient", $"{prefix} Line 1", null, $"{prefix} City", $"{prefix} Region", "1000", "US");

    private static OrderLineItem CreateItem(string sku) =>
        new(
            OrderProductId.Create(),
            OrderVariantId.Create(),
            new OrderSku(sku),
            "Test Product",
            new OrderMoney(5m, "USD"),
            1);

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}
