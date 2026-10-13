using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Persistence.Stores;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Coverage for the Phase 23 F3 daily-series projections: lightweight filtered selects that must
/// translate on both SQLite and SQL Server (no date-part grouping in the database — bucketing
/// runs in memory). Runs on SQLite to prove the translation.
/// </summary>
public sealed class DailyPointsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Base = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _ordersConnection;
    private readonly SqliteConnection _paymentsConnection;

    public DailyPointsStoreTests()
    {
        _ordersConnection = new SqliteConnection("Data Source=:memory:");
        _ordersConnection.Open();
        _paymentsConnection = new SqliteConnection("Data Source=:memory:");
        _paymentsConnection.Open();
    }

    [Fact]
    public async Task GetCreatedPointsAsync_ReturnsOnlyRowsInsideWindow()
    {
        var customerId = new CustomerId(Guid.NewGuid());
        await using (var seed = CreateOrdersContext())
        {
            seed.Orders.Add(CreateOrder(customerId, Base.AddDays(-10)));
            seed.Orders.Add(CreateOrder(customerId, Base.AddDays(-3)));
            seed.Orders.Add(CreateOrder(customerId, Base.AddHours(-1)));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateOrdersContext();
        var store = new OrderStore(context);

        var points = await store.GetCreatedPointsAsync(Base.AddDays(-5));

        Assert.Equal(2, points.Count);
        Assert.All(points, point => Assert.True(point.CreatedAtUtc >= Base.AddDays(-5)));
    }

    [Fact]
    public async Task GetCreatedPointsAsync_EmptyWindow_ReturnsEmpty()
    {
        await using var context = CreateOrdersContext();
        var store = new OrderStore(context);

        var points = await store.GetCreatedPointsAsync(Base.AddDays(1));

        Assert.Empty(points);
    }

    [Fact]
    public async Task GetDailyPointsAsync_ReturnsAmountCurrencyStatusInsideWindow()
    {
        await using (var seed = CreatePaymentsContext())
        {
            AddPayment(seed, Base.AddDays(-10), 10.00m, "USD", succeeded: true);
            AddPayment(seed, Base.AddDays(-2), 100.00m, "USD", succeeded: true);
            AddPayment(seed, Base.AddDays(-1), 50.00m, "EUR", succeeded: true);
            AddPayment(seed, Base.AddDays(-1), 999.00m, "USD", succeeded: false);
            await seed.SaveChangesAsync();
        }

        await using var context = CreatePaymentsContext();
        var store = new PaymentAdminStore(context);

        var points = await store.GetDailyPointsAsync(Base.AddDays(-5));

        Assert.Equal(3, points.Count);
        Assert.Contains(points, point =>
            point.Amount == 100.00m && point.Currency == "USD" && point.Status == PaymentStatus.Succeeded);
        Assert.Contains(points, point =>
            point.Amount == 50.00m && point.Currency == "EUR" && point.Status == PaymentStatus.Succeeded);
        Assert.Contains(points, point =>
            point.Amount == 999.00m && point.Currency == "USD" && point.Status == PaymentStatus.Pending);
    }

    [Fact]
    public async Task GetDailyPointsAsync_EmptyWindow_ReturnsEmpty()
    {
        await using var context = CreatePaymentsContext();
        var store = new PaymentAdminStore(context);

        var points = await store.GetDailyPointsAsync(Base.AddDays(1));

        Assert.Empty(points);
    }

    public void Dispose()
    {
        _ordersConnection.Dispose();
        _paymentsConnection.Dispose();
    }

    private OrdersDbContext CreateOrdersContext()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlite(_ordersConnection)
            .Options;

        var context = new OrdersDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        context.Database.EnsureCreated();

        return context;
    }

    private PaymentsDbContext CreatePaymentsContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(_paymentsConnection)
            .Options;

        var context = new PaymentsDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        context.Database.EnsureCreated();

        return context;
    }

    private static Order CreateOrder(CustomerId customerId, DateTimeOffset at) =>
        Order.Place(
            OrderId.Create(),
            customerId,
            OrderNumber.Create(),
            new OrderAddress("Recipient", "Line 1", null, "City", "Region", "1000", "US"),
            new OrderAddress("Recipient", "Line 1", null, "City", "Region", "1000", "US"),
            [
                new OrderLineItem(
                    OrderProductId.Create(),
                    OrderVariantId.Create(),
                    new OrderSku("SKU-1"),
                    "Test Product",
                    new OrderMoney(10m, "USD"),
                    1),
            ],
            at);

    private static void AddPayment(
        PaymentsDbContext context,
        DateTimeOffset createdAt,
        decimal amount,
        string currency,
        bool succeeded)
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), amount, currency, Guid.NewGuid());
        if (succeeded)
            payment.Succeed($"pi_{Guid.NewGuid():N}");

        context.Set<Payment>().Add(payment);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var tracked = context.Set<Payment>().Single(item => item.Id == payment.Id);
        context.Entry(tracked).Property(item => item.CreatedAtUtc).CurrentValue = createdAt;
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}
