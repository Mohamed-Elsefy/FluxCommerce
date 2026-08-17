using System.Globalization;
using System.Text;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.DependencyInjection;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using InventorySku = FluxCommerce.Modules.Inventory.Domain.ValueObjects.InventorySku;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 7 P0 regression: the consumer infrastructure must always execute a handler through the
/// <c>IIdempotentProcessor</c> implementation owned by the handler's own module, even when several
/// modules are composed in one container. The production API registers Orders infrastructure and then
/// Inventory infrastructure, so a container-wide <c>IIdempotentProcessor</c> resolution would be
/// last-registration-wins (Inventory) and an Orders consumer would write its <c>ProcessedMessage</c>
/// into the Inventory database — violating ARCHITECTURE.md §14.1.1 ("one module never touches another
/// module's ProcessedMessages") and silently detaching the projection's business effect from its
/// idempotency record.
///
/// Each test builds a production-like container graph (Orders registered before Inventory, exactly like
/// the API) and a single real consumer queue on the test exchange, then publishes the same event twice
/// (idempotent duplicate) and asserts that the Orders consumer committed its projection AND its
/// <c>ProcessedMessage</c> to the ORDERS database only, while the Inventory consumer committed its
/// business effect AND its <c>ProcessedMessage</c> to the INVENTORY database only. The second scenario
/// proves the fix never regressed the Inventory side (which, registered last, was previously the only
/// processor reachable via the interface).
/// </summary>
[Collection(ConsumerModuleIsolationTestCollection.Name)]
public class ConsumerModuleIsolationIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly RabbitMQOrdersConsumerTestFixture _ordersFixture;
    private readonly RabbitMQConsumerTestFixture _inventoryFixture;

    public ConsumerModuleIsolationIntegrationTests(
        RabbitMQOrdersConsumerTestFixture ordersFixture,
        RabbitMQConsumerTestFixture inventoryFixture)
    {
        _ordersFixture = ordersFixture;
        _inventoryFixture = inventoryFixture;
    }

    [Fact]
    public async Task OrdersConsumer_WithInventoryRegisteredLast_UsesOrdersProcessor_WritesOnlyOrdersDatabase()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _ordersFixture.SeedOrderAsync(orderId, customerId);
        var @event = CreateOrderCreated(orderId, customerId);
        await using var provider = BuildConsumerProvider<OrderSummaryProjectionConsumer, OrdersIdempotentProcessor>(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // First delivery plus an identical duplicate delivery: the consumer must acknowledge the
            // duplicate without a second effect (idempotency identity = EventId + HandlerName).
            await PublishAsync(provider, @event);
            await PublishAsync(provider, @event);

            await WaitForProjectionAsync(orderId);
            await WaitForProcessedInOrdersAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            // The Orders idempotent-processing boundary committed the projection AND its ProcessedMessage
            // to the ORDERS database — the consumer resolved the descriptor's OrdersIdempotentProcessor.
            Assert.Equal(1, await CountProjectionsAsync(orderId));
            Assert.Equal(1, await CountProcessedInOrdersAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));

            // The INVENTORY database (registered last) must NOT contain the Orders consumer's
            // ProcessedMessage: the consumer never executed through Inventory's processor.
            Assert.Equal(0, await CountProcessedInInventoryAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, @event.EventId, skus: []);
        }
    }

    [Fact]
    public async Task InventoryConsumer_WithOrdersRegisteredFirst_UsesInventoryProcessor_WritesOnlyInventoryDatabase()
    {
        var queueName = UniqueQueueName();
        var @event = CreateOrderCreated(Guid.NewGuid(), Guid.NewGuid());
        var recorder = new TestConsumerRecorder();
        var sku = RabbitMQConsumerIntegrationTests.EffectSku(@event.EventId);
        await using var provider = BuildConsumerProvider<InventoryEffectOrderCreatedHandler, InventoryIdempotentProcessor>(queueName, recorder);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);
            await PublishAsync(provider, @event);

            await WaitForProcessedInInventoryAsync(@event.EventId, InventoryEffectOrderCreatedHandler.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            // The Inventory idempotent-processing boundary committed the business effect AND its
            // ProcessedMessage to the INVENTORY database — the consumer resolved the descriptor's
            // InventoryIdempotentProcessor, not the container-wide interface.
            Assert.Equal(1, await CountItemsAsync(sku));
            Assert.Equal(1, await CountProcessedInInventoryAsync(@event.EventId, InventoryEffectOrderCreatedHandler.StableHandlerName));

            // The ORDERS database (registered first) must NOT contain the Inventory consumer's
            // ProcessedMessage: the Inventory consumer never executed through Orders' processor.
            Assert.Equal(0, await CountProcessedInOrdersAsync(@event.EventId, InventoryEffectOrderCreatedHandler.StableHandlerName));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId: null, @event.EventId, skus: [sku]);
        }
    }

    private ServiceProvider BuildConsumerProvider<THandler, TProcessor>(string queueName, TestConsumerRecorder? recorder = null)
        where THandler : class, IIntegrationEventHandler<OrderCreatedIntegrationEvent>
        where TProcessor : class, IIdempotentProcessor
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();

        // Production-like container graph (mirrors the API's AddInfrastructureLayer ordering): Orders
        // infrastructure is registered BEFORE Inventory, so resolving the container-wide
        // IIdempotentProcessor would pick Inventory's (registered last). The consumer infrastructure
        // must resolve the processor declared on each consumer's descriptor instead.
        services.AddOrdersInfrastructure(RabbitMQOrdersConsumerTestFixture.SqlServerConnectionString);
        services.AddInventoryInfrastructure(RabbitMQConsumerTestFixture.SqlServerConnectionString);

        services.AddRabbitMQ(_ordersFixture.RabbitMQConnectionString);
        services.AddRabbitMQPublisher(RabbitMQOrdersConsumerTestFixture.TestExchangeName);
        if (recorder is not null)
            services.AddSingleton(recorder);
        services.AddRabbitMQConsumer(RabbitMQOrdersConsumerTestFixture.TestExchangeName, queueName);
        services.AddIntegrationEventConsumer<OrderCreatedIntegrationEvent, THandler, TProcessor>();

        return services.BuildServiceProvider();
    }

    private static async Task<RabbitMQConsumerBackgroundService> StartConsumerAsync(ServiceProvider provider)
    {
        var consumer = provider.GetServices<IHostedService>()
            .OfType<RabbitMQConsumerBackgroundService>()
            .Single();
        await consumer.StartAsync(CancellationToken.None);
        return consumer;
    }

    private static async Task PublishAsync(IServiceProvider provider, OrderCreatedIntegrationEvent @event)
    {
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var confirmed = await publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, @event);
        Assert.True(confirmed);
    }

    private static async Task WaitForConsumerAsync(IServiceProvider provider, string queueName)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await ConsumerCountAsync(provider, queueName) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"No consumer connected to queue '{queueName}' within {Timeout}.");
    }

    private async Task WaitForProjectionAsync(Guid orderId)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountProjectionsAsync(orderId) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Order summary projection for order '{orderId}' was not persisted within {Timeout}.");
    }

    private async Task WaitForProcessedInOrdersAsync(Guid eventId, string handler)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountProcessedInOrdersAsync(eventId, handler) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Event {eventId} was not processed by '{handler}' in the Orders database within {Timeout}.");
    }

    private async Task WaitForProcessedInInventoryAsync(Guid eventId, string handler)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountProcessedInInventoryAsync(eventId, handler) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Event {eventId} was not processed by '{handler}' in the Inventory database within {Timeout}.");
    }

    private static async Task WaitForQueueEmptyAsync(IServiceProvider provider, string queueName)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await GetMessageCountAsync(provider, queueName) == 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Queue '{queueName}' did not drain within {Timeout}.");
    }

    private async Task<int> CountProjectionsAsync(Guid orderId)
    {
        using var scope = _ordersFixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OrderSummaryProjections.CountAsync(projection => projection.OrderId == orderId);
    }

    private async Task<int> CountProcessedInOrdersAsync(Guid eventId, string handler)
    {
        using var scope = _ordersFixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
    }

    private async Task<int> CountProcessedInInventoryAsync(Guid eventId, string handler)
    {
        using var scope = _inventoryFixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
    }

    private async Task<int> CountItemsAsync(string sku)
    {
        using var scope = _inventoryFixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryItems.CountAsync(item => item.Id == new InventorySku(sku));
    }

    private static async Task<uint> ConsumerCountAsync(IServiceProvider provider, string queueName)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        try
        {
            return await channel.ConsumerCountAsync(queueName, CancellationToken.None);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    private static async Task<uint> GetMessageCountAsync(IServiceProvider provider, string queueName)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        try
        {
            var result = await channel.QueueDeclarePassiveAsync(queueName, CancellationToken.None);
            return result.MessageCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    private async Task CleanupAsync(
        IServiceProvider provider,
        string queueName,
        Guid? orderId,
        Guid eventId,
        IEnumerable<string> skus)
    {
        var key = eventId.ToString();
        var itemSkus = skus.Select(sku => new InventorySku(sku)).ToList();

        using (var scope = _ordersFixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            if (orderId is not null)
                await context.OrderSummaryProjections
                    .Where(projection => projection.OrderId == orderId.Value)
                    .ExecuteDeleteAsync();
            await context.ProcessedMessages
                .Where(message => message.Key == key)
                .ExecuteDeleteAsync();
            if (orderId is not null)
                await context.Orders
                    .Where(order => order.Id == new OrderId(orderId.Value))
                    .ExecuteDeleteAsync();
        }

        using (var inventoryScope = _inventoryFixture.Services.CreateScope())
        {
            var context = inventoryScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await context.ProcessedMessages
                .Where(message => message.Key == key)
                .ExecuteDeleteAsync();
            if (itemSkus.Count > 0)
                await context.InventoryItems
                    .Where(item => itemSkus.Contains(item.Id))
                    .ExecuteDeleteAsync();
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static Task DeleteQueueAsync(IServiceProvider provider, string queueName) =>
        Task.Run(async () =>
        {
            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            await TryDeleteQueueAsync(channel, DeadLetterQueueName(queueName));
            await TryDeleteQueueAsync(channel, queueName);
            try
            {
                await channel.ExchangeDeleteAsync(
                    exchange: DeadLetterExchangeName(queueName),
                    ifUnused: false,
                    noWait: false,
                    cancellationToken: default);
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
            }
        });

    private static async Task TryDeleteQueueAsync(IChannel channel, string queueName)
    {
        try
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
        }
    }

    private static string UniqueQueueName() => $"it.isolation.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static OrderCreatedIntegrationEvent CreateOrderCreated(Guid orderId, Guid customerId) =>
        new(
            orderId,
            customerId,
            $"ORD-ISO-{Guid.NewGuid():N}"[..16],
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = Guid.NewGuid()
        };

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;

        public string? Email => null;

        public string[] Roles => [];

        public bool IsAuthenticated => false;
    }
}
