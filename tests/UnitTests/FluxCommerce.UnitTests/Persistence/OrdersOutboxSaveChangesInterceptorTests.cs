using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Events;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Outbox;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Phase 6.3: the Orders outbox interceptor appends Outbox records inside the same SaveChangesAsync
/// that writes the business state. A placed order and a cancelled order both produce exactly one
/// selected outbox record; a save without a selected domain event produces none.
/// </summary>
public class OrdersOutboxSaveChangesInterceptorTests : IDisposable
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 8, 10, 8, 15, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public OrdersOutboxSaveChangesInterceptorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task PlacingOrder_WritesOrderCreatedOutbox_Atomically()
    {
        await using var context = CreateContext();

        var order = CreateOrder(OrderId.Create(), Guid.NewGuid());
        var createdEvent = order.GetDomainEvents().OfType<OrderCreated>().Single();

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var message = await context.OutboxMessages.SingleAsync(m => m.EventId == createdEvent.EventId);

        Assert.Equal(OrderCreatedIntegrationEvent.EventType, message.Type);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.CausationId);
        Assert.Equal(createdEvent.OccurredAtUtc, message.OccurredAtUtc);
        Assert.NotEqual(Guid.Empty, message.CorrelationId);
    }

    [Fact]
    public async Task CancellingOrder_WritesOrderCancelledOutbox()
    {
        var orderId = OrderId.Create();
        var userId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Orders.Add(CreateOrder(orderId, userId));
            await context.SaveChangesAsync();
        }

        await using var context2 = CreateContext();
        var tracked = context2.Orders.Include(o => o.LineItems).Single(o => o.Id == orderId);

        tracked.Cancel(CreatedAtUtc);
        var cancelledEvent = tracked.GetDomainEvents().OfType<OrderCancelled>().Single();

        await context2.SaveChangesAsync();

        var message = await context2.OutboxMessages.SingleAsync(m => m.EventId == cancelledEvent.EventId);

        Assert.Equal(OrderCancelledIntegrationEvent.EventType, message.Type);
        Assert.Equal(OutboxStatus.Pending, message.Status);
    }

    [Fact]
    public async Task SavingWithoutSelectedDomainEvents_WritesNoOutbox()
    {
        await using var context = CreateContext();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Type = "orders.manual",
            Payload = "{}",
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            Status = OutboxStatus.Pending,
            RetryCount = 0
        });

        await context.SaveChangesAsync();

        Assert.Single(await context.OutboxMessages.ToListAsync());
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private OrdersDbContext CreateContext()
    {
        var outboxInterceptor = new OrdersOutboxSaveChangesInterceptor(
            new OrdersOutboxMessageFactory(new FixedCorrelationIdProvider(Guid.NewGuid())));

        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(outboxInterceptor)
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

    private static OrderAddress CreateAddress() =>
        new("Shipping Recipient", "Shipping Line 1", null, "Shipping City", "Region", "1000", "US");

    private sealed class FixedCorrelationIdProvider : ICorrelationIdProvider
    {
        private readonly Guid _value;

        public FixedCorrelationIdProvider(Guid value) => _value = value;

        public Guid GetCorrelationId() => _value;
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}