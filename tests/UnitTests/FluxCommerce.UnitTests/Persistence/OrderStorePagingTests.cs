using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// C4 coverage: order history paging returns newest-first pages with a bounded key window
/// and an empty page (not a full scan failure) past the end.
/// </summary>
public sealed class OrderStorePagingTests : IDisposable
{
    private static readonly DateTimeOffset Base = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public OrderStorePagingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task GetMyOrdersPageAsync_ReturnsNewestFirstPages_AndEmptyPastEnd()
    {
        var customerId = new CustomerId(Guid.NewGuid());
        await using (var seed = CreateContext())
        {
            for (var i = 0; i < 5; i++)
                seed.Orders.Add(CreateOrder(customerId, Base.AddHours(i)));
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var store = new OrderStore(ctx);

        var page1 = await store.GetMyOrdersPageAsync(customerId, 1, 2);
        var page2 = await store.GetMyOrdersPageAsync(customerId, 2, 2);
        var page3 = await store.GetMyOrdersPageAsync(customerId, 3, 2);
        var page4 = await store.GetMyOrdersPageAsync(customerId, 4, 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal([Base.AddHours(4), Base.AddHours(3)], page1.Items.Select(i => i.CreatedAtUtc).ToList());
        Assert.Equal([Base.AddHours(2), Base.AddHours(1)], page2.Items.Select(i => i.CreatedAtUtc).ToList());
        Assert.Equal([Base.AddHours(0)], page3.Items.Select(i => i.CreatedAtUtc).ToList());
        Assert.Empty(page4.Items);
        Assert.Equal(5, page4.TotalCount);
    }

    [Fact]
    public async Task GetMyOrdersPageAsync_IsolatesCustomers()
    {
        await using (var seed = CreateContext())
        {
            seed.Orders.Add(CreateOrder(new CustomerId(Guid.NewGuid()), Base));
            seed.Orders.Add(CreateOrder(new CustomerId(Guid.NewGuid()), Base.AddHours(1)));
            await seed.SaveChangesAsync();
        }

        var mine = new CustomerId(Guid.NewGuid());
        await using (var seed = CreateContext())
        {
            seed.Orders.Add(CreateOrder(mine, Base.AddHours(2)));
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var store = new OrderStore(ctx);

        var page = await store.GetMyOrdersPageAsync(mine, 1, 10);

        Assert.Equal(1, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(Base.AddHours(2), item.CreatedAtUtc);
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

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}
