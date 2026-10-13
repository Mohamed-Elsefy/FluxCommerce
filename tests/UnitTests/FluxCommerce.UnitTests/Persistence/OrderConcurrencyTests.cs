using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// A5 coverage: <c>Order.Version</c> is a concurrency token, so a concurrent
/// Confirm (payment success) vs Cancel (user/expiry path) cannot silently
/// last-writer-win — the loser gets <see cref="DbUpdateConcurrencyException"/>.
/// </summary>
public sealed class OrderConcurrencyTests : IDisposable
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 10, 8, 15, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public OrderConcurrencyTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task SaveChanges_ConcurrentConfirmAndCancel_SecondWriterThrowsConcurrencyConflict()
    {
        var orderId = OrderId.Create();
        await using (var seed = CreateContext())
        {
            seed.Orders.Add(CreateOrder(orderId));
            await seed.SaveChangesAsync();
        }

        await using var first = CreateContext();
        await using var second = CreateContext();

        var confirmSide = await first.Orders.FirstAsync(o => o.Id == orderId);
        var cancelSide = await second.Orders.FirstAsync(o => o.Id == orderId);

        Assert.True(confirmSide.Confirm(UtcNow).IsSuccess);
        await first.SaveChangesAsync();

        Assert.True(cancelSide.Cancel(UtcNow).IsSuccess);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    public void Dispose() => _connection.Dispose();

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

    private static Order CreateOrder(OrderId orderId) =>
        Order.Place(
            orderId,
            new CustomerId(Guid.NewGuid()),
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
            UtcNow);

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}
