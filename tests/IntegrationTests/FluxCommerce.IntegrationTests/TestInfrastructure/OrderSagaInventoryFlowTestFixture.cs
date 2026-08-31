using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using InventoryOrderId = FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId;
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Phase 8.4 command-flow fixture. Composes the Orders and Inventory modules exactly like the API host
/// (Orders first, Inventory second) together with the real RabbitMQ broker on the dedicated test
/// exchange, so the full <c>Saga → Outbox → RabbitMQ → Inventory Consumer → Inventory Reservation</c>
/// chain can be exercised end to end: the saga orchestrator stages the reservation-request outbox, the
/// Orders dispatcher publishes it, and the real Inventory reservation-request consumer executes the
/// reservation through the Inventory idempotent-processing boundary. Orders are seeded with
/// <see cref="SeedOrderAsync"/> and items with <see cref="SeedItemAsync"/> (hydrated aggregates, so no
/// domain events and no outbox side effects).
/// </summary>
public sealed class OrderSagaInventoryFlowTestFixture : IAsyncDisposable
{
    public const string ConnectionEnvironmentVariable = "FLUXCOMMERCE_RABBITMQ_CONNECTION";

    public const string TestExchangeName = "fluxcommerce.events.it";

    public const string OrdersConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Orders;User Id=sa;Password=FluxCommerceDBPass3030;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public const string InventoryConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Inventory;User Id=sa;Password=FluxCommerceDBPass3030;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private const string DefaultRabbitMQConnection = "amqp://fluxcommerce:FluxCommerce_SR_MB_3030%21%21@localhost:5672/";

    private readonly ServiceProvider _migrationProvider;

    public OrderSagaInventoryFlowTestFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddOrdersInfrastructure(OrdersConnectionString);
        services.AddInventoryInfrastructure(InventoryConnectionString);

        _migrationProvider = services.BuildServiceProvider();

        using (var ordersScope = _migrationProvider.CreateScope())
        {
            ordersScope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.Migrate();
        }

        using (var inventoryScope = _migrationProvider.CreateScope())
        {
            inventoryScope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.Migrate();
        }

        VerifyConnectivity();
    }

    public string RabbitMQConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable) ?? DefaultRabbitMQConnection;

    public IServiceProvider Services => _migrationProvider;

    /// <summary>
    /// Builds the production-like provider for the command-flow chain: Orders infrastructure (DbContext,
    /// saga orchestrator, outbox dispatcher) + Inventory infrastructure (DbContext, reservation handler,
    /// idempotency) + RabbitMQ publishing and consuming, with the real Inventory reservation-request
    /// consumer and the real Orders result consumers (<c>InventoryReservedConsumer</c>,
    /// <c>InventoryReservationFailedConsumer</c>) sharing the queue — so the full
    /// <c>Saga → Outbox → RabbitMQ → Inventory → Result Event → Orders</c> loop can be exercised end to
    /// end. The hosted <c>RabbitMQConsumerBackgroundService</c> is started per test so the outbox hosted
    /// services never run (the dispatchers are driven directly). The optional <paramref name="configure"/>
    /// callback tunes the consumer host (e.g. concurrency for the 8.8 concurrent-result verification).
    /// </summary>
    public ServiceProvider BuildProvider(string queueName, Action<RabbitMQConsumerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddOrdersInfrastructure(OrdersConnectionString);
        services.AddInventoryInfrastructure(InventoryConnectionString);
        services.AddRabbitMQ(RabbitMQConnectionString);
        services.AddRabbitMQPublisher(TestExchangeName);
        services.AddRabbitMQConsumer(TestExchangeName, queueName, configure);
        services.AddIntegrationEventConsumer<InventoryReservationRequestedIntegrationEvent, InventoryReservationRequestedConsumer, InventoryIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservedIntegrationEvent, InventoryReservedConsumer, OrdersIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservationFailedIntegrationEvent, InventoryReservationFailedConsumer, OrdersIdempotentProcessor>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Phase 8.7: the production-like provider for the compensation flow. It registers everything
    /// <see cref="BuildProvider"/> does plus the two Phase 8.7 consumers on the same queue — the
    /// Inventory release-request consumer (<c>inventory.release_requested</c>) and the Orders
    /// compensation consumer (<c>orders.order_cancelled</c>) — so the full
    /// <c>Order Cancelled → Saga → Release Command → Inventory Consumer → Reservation Released</c> leg
    /// can be exercised end to end. The hosted <c>RabbitMQConsumerBackgroundService</c> is started per
    /// test so the outbox hosted services never run (the dispatchers are driven directly). The optional
    /// <paramref name="configure"/> callback tunes the consumer host (e.g. concurrency for the 8.8
    /// concurrent-result verification).
    /// </summary>
    public ServiceProvider BuildCompensationProvider(string queueName, Action<RabbitMQConsumerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddOrdersInfrastructure(OrdersConnectionString);
        services.AddInventoryInfrastructure(InventoryConnectionString);
        services.AddRabbitMQ(RabbitMQConnectionString);
        services.AddRabbitMQPublisher(TestExchangeName);
        services.AddRabbitMQConsumer(TestExchangeName, queueName, configure);
        services.AddIntegrationEventConsumer<InventoryReservationRequestedIntegrationEvent, InventoryReservationRequestedConsumer, InventoryIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservedIntegrationEvent, InventoryReservedConsumer, OrdersIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservationFailedIntegrationEvent, InventoryReservationFailedConsumer, OrdersIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReleaseRequestedIntegrationEvent, InventoryReleaseRequestedConsumer, InventoryIdempotentProcessor>();
        services.AddIntegrationEventConsumer<OrderCancelledIntegrationEvent, OrderCancelledSagaConsumer, OrdersIdempotentProcessor>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Phase 11.10: the production-like provider for the reservation-expiry flow. It registers
    /// everything <see cref="BuildProvider"/> does plus the Orders reservation-expiry consumer
    /// (<c>inventory.reservation_expired</c>) on the same queue — so the full
    /// <c>Inventory Reservation Expiration → Inventory Outbox → RabbitMQ → Orders Expiry Consumer → Saga → Order Cancelled</c>
    /// chain can be exercised end to end. The hosted <c>RabbitMQConsumerBackgroundService</c> is started
    /// per test so the outbox hosted services never run (the dispatchers are driven directly). The optional
    /// <paramref name="configure"/> callback tunes the consumer host (e.g. concurrency for the 11.10
    /// concurrent-expiry verification).
    /// </summary>
    public ServiceProvider BuildExpiryProvider(string queueName, Action<RabbitMQConsumerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddOrdersInfrastructure(OrdersConnectionString);
        services.AddInventoryInfrastructure(InventoryConnectionString);
        services.AddRabbitMQ(RabbitMQConnectionString);
        services.AddRabbitMQPublisher(TestExchangeName);
        services.AddRabbitMQConsumer(TestExchangeName, queueName, configure);
        services.AddIntegrationEventConsumer<InventoryReservationRequestedIntegrationEvent, InventoryReservationRequestedConsumer, InventoryIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservedIntegrationEvent, InventoryReservedConsumer, OrdersIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservationFailedIntegrationEvent, InventoryReservationFailedConsumer, OrdersIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservationExpiredIntegrationEvent, InventoryReservationExpiredConsumer, OrdersIdempotentProcessor>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Phase 8.9: the production-like provider for the distributed-trace verification. It registers
    /// everything <see cref="BuildProvider"/> does plus the <c>orders.order_created</c> saga-boot
    /// consumer, so the complete workflow can be driven end to end from a published OrderCreated: the
    /// saga orchestrator boots, the Orders outbox dispatcher publishes the reservation request, the real
    /// Inventory reservation consumer executes it, the Inventory outbox dispatcher publishes the result,
    /// and the Orders result consumer completes the saga — all inside one distributed trace. The optional
    /// captured loggers replace the default processor and orchestrator loggers so the test can assert on
    /// the Phase 8.9 structured fields (<c>TraceId</c>/<c>SpanId</c>/<c>PreviousState</c>/<c>NewState</c>).
    /// </summary>
    public ServiceProvider BuildTraceProvider(
        string queueName,
        IntegrationTestCapturingLogger<IntegrationEventMessageProcessor>? processorLogger = null,
        IntegrationTestCapturingLogger<OrderSagaOrchestrator>? orchestratorLogger = null,
        Action<RabbitMQConsumerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (processorLogger is not null)
            services.AddSingleton<ILogger<IntegrationEventMessageProcessor>>(processorLogger);
        if (orchestratorLogger is not null)
            services.AddSingleton<ILogger<OrderSagaOrchestrator>>(orchestratorLogger);
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();
        services.AddOrdersInfrastructure(OrdersConnectionString);
        services.AddInventoryInfrastructure(InventoryConnectionString);
        services.AddRabbitMQ(RabbitMQConnectionString);
        services.AddRabbitMQPublisher(TestExchangeName);
        services.AddRabbitMQConsumer(TestExchangeName, queueName, configure);
        services.AddIntegrationEventConsumer<OrderCreatedIntegrationEvent, OrderCreatedSagaConsumer, OrdersIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservationRequestedIntegrationEvent, InventoryReservationRequestedConsumer, InventoryIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservedIntegrationEvent, InventoryReservedConsumer, OrdersIdempotentProcessor>();
        services.AddIntegrationEventConsumer<InventoryReservationFailedIntegrationEvent, InventoryReservationFailedConsumer, OrdersIdempotentProcessor>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds an order with a single line item (the given SKU x quantity) directly in the orders
    /// database. The aggregate is hydrated (no domain events) so seeding produces no outbox side
    /// effects — only the saga orchestrator stages reservation commands.
    /// </summary>
    public async Task<Order> SeedOrderAsync(Guid orderId, Guid customerId, string sku, int quantity)
    {
        using var scope = _migrationProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = Order.Hydrate(
            new OrdersOrderId(orderId),
            new CustomerId(customerId),
            new OrderNumber($"ORD-SAGA-{Guid.NewGuid():N}"[..Math.Min(40, 8 + 12)]),
            CreateAddress(),
            CreateAddress(),
            OrderStatus.Pending,
            Order.InitialVersion,
            new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero),
            [
                new OrderLineItem(
                    OrderProductId.Create(),
                    OrderVariantId.Create(),
                    new OrderSku(sku),
                    "Saga Test Product",
                    new OrderMoney(25.00m, "USD"),
                    quantity),
            ]);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order;
    }

    /// <summary>
    /// Seeds an inventory item with the given on-hand quantity directly in the inventory database
    /// (hydrated, so no domain events and no outbox side effects).
    /// </summary>
    public async Task SeedItemAsync(string sku, int onHandQuantity)
    {
        using var scope = _migrationProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var item = InventoryItem.Hydrate(
            new InventorySku(sku),
            onHandQuantity,
            reservedQuantity: 0,
            InventoryItem.InitialVersion,
            new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero));

        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an inventory item together with a Pending reservation for the given order (hydrated
    /// aggregates, so no domain events and no outbox side effects), so a test can deterministically
    /// produce the pre-release state the release consumer would observe: the item holds
    /// <paramref name="quantity"/> reserved and the reservation expires at <paramref name="expiresAt"/>.
    /// Returns the reservation's id.
    /// </summary>
    public async Task<Guid> SeedReservationAsync(
        string sku,
        Guid orderId,
        int quantity,
        DateTimeOffset expiresAt,
        int onHand)
    {
        var now = DateTimeOffset.UtcNow;
        var item = InventoryItem.Create(new InventorySku(sku), onHand, now);
        item.Reserve(quantity, now);

        var reservation = InventoryReservation.Hydrate(
            InventoryReservationId.Create(),
            new InventoryOrderId(orderId),
            item.Sku,
            quantity,
            InventoryReservationStatus.Pending,
            expiresAt,
            InventoryReservation.InitialVersion,
            now,
            now);

        using var scope = _migrationProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        context.InventoryItems.Add(item);
        context.InventoryReservations.Add(reservation);
        await context.SaveChangesAsync();

        return reservation.Id.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await _migrationProvider.DisposeAsync();
    }

    private static OrderAddress CreateAddress() =>
        new("Saga Recipient", "1 Saga St", null, "Saga City", "Region", "1000", "US");

    private void VerifyConnectivity()
    {
        try
        {
            var provider = new RabbitMQConnectionProvider(RabbitMQConnectionString);
            var channel = provider.CreateChannelAsync().GetAwaiter().GetResult();
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "The Order/Saga Inventory command-flow integration tests require a reachable RabbitMQ broker and fail fast when it is unavailable. " +
                $"Start it with 'docker compose up -d rabbitmq' or override the connection through the " +
                $"'{ConnectionEnvironmentVariable}' environment variable. Underlying error: {exception.Message}",
                exception);
        }
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;

        public string? Email => null;

        public string[] Roles => [];

        public bool IsAuthenticated => false;
    }
}