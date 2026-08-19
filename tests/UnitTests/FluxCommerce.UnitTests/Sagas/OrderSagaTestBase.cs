using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Outbox;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 8.3 shared harness for the Order/Inventory Saga orchestration tests: an in-memory SQLite
/// Orders DbContext (with the real outbox interceptor and stores, so the staged saga + outbox records
/// behave exactly as in the module) plus factories for the orchestrator and test orders.
/// </summary>
public abstract class OrderSagaTestBase : IDisposable
{
    protected static readonly DateTimeOffset PlacedAtUtc = new(2026, 8, 19, 8, 15, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    protected OrderSagaTestBase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected OrdersDbContext CreateContext()
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

    protected static OrderSagaOrchestrator CreateOrchestrator(
        OrdersDbContext context,
        IDateTimeProvider? dateTimeProvider = null,
        ReservationRequestOptions? options = null,
        ILogger<OrderSagaOrchestrator>? logger = null)
    {
        return new OrderSagaOrchestrator(
            new OrderSagaStore(context),
            new OrderStore(context),
            context,
            new SagaOutboxMessageFactory(),
            dateTimeProvider ?? new FakeDateTimeProvider(),
            Options.Create(options ?? new ReservationRequestOptions()),
            logger ?? new CapturingLogger<OrderSagaOrchestrator>());
    }

    /// <summary>
    /// Places an order in its OWN context (like production, where checkout commits in a separate
    /// transaction) and returns the detached order. The saga tests then run the orchestrator against a
    /// fresh context on the same connection, so the placed order's <c>OrderCreated</c> domain event
    /// stays committed with the order and is never re-emitted by a later save.
    /// </summary>
    protected async Task<Order> CreatePlacedOrderAsync(params (string Sku, int Quantity)[] items)
    {
        await using var context = CreateContext();

        var order = Order.Place(
            OrderId.Create(),
            new CustomerId(Guid.NewGuid()),
            OrderNumber.Create(),
            CreateAddress(),
            CreateAddress(),
            items
                .Select(item => new OrderLineItem(
                    OrderProductId.Create(),
                    OrderVariantId.Create(),
                    new OrderSku(item.Sku),
                    "Test Product",
                    new OrderMoney(10m, "USD"),
                    item.Quantity))
                .ToList(),
            PlacedAtUtc);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order;
    }

    protected static OrderAddress CreateAddress() =>
        new("Shipping Recipient", "Shipping Line 1", null, "Shipping City", "Region", "1000", "US");

    public void Dispose() => _connection.Dispose();

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