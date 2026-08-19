using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;
using InventoryOrderId = FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId;
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 8.4 verification: the complete reservation command flow against the real SQL Server engines and
/// the real RabbitMQ broker:
///
///   Order Saga → Outbox → RabbitMQ → Inventory Consumer → Inventory Reservation
///
/// The saga orchestrator is driven exactly as the OrderCreated saga consumer would (8.3), staging the
/// saga and one <c>inventory.reservation_requested</c> per line item; the Orders outbox dispatcher
/// publishes the command; the real Inventory reservation-request consumer executes the reservation
/// through the Inventory idempotent-processing boundary. A redelivered OrderCreated (saga already
/// exists) issues no further commands, so exactly one reservation request reaches the broker.
/// </summary>
[Collection(OrderSagaInventoryFlowTestCollection.Name)]
public class OrderSagaInventoryCommandFlowIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly OrderSagaInventoryFlowTestFixture _fixture;

    public OrderSagaInventoryCommandFlowIntegrationTests(OrderSagaInventoryFlowTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Saga_ReservationCommandFlow_ReservesInventory_EndToEnd()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sku = UniqueSku("8.4-FLOW").ToUpperInvariant();
        const int quantity = 3;
        var correlationId = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        await _fixture.SeedItemAsync(sku, 10);
        await _fixture.SeedOrderAsync(orderId, customerId, sku, quantity);
        await using var provider = _fixture.BuildProvider(queueName);

        var commandEventId = Guid.Empty;
        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // The saga orchestrator stages the saga + reservation-request outbox records (this is the
            // work the OrderCreated saga consumer performs inside the Orders idempotency boundary).
            using (var scope = provider.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
                var result = await orchestrator.StartFromOrderCreatedAsync(
                    new OrdersOrderId(orderId),
                    correlationId,
                    causationId: null,
                    occurredAtUtc);

                Assert.True(result.SagaCreated);
                var command = Assert.Single(result.Commands);
                commandEventId = command.EventId;
                Assert.Equal(orderId, command.OrderId);
                Assert.Equal(sku, command.Sku);
                Assert.Equal(quantity, command.Quantity);
                Assert.Equal(correlationId, command.CorrelationId);

                await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();
            }

            // The Orders outbox dispatcher publishes the reservation request to RabbitMQ.
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            // The Inventory consumer executes the reservation through the idempotent-processing
            // boundary (Business State + ProcessedMessage commit atomically) and ACKs.
            await WaitForReservationAsync(orderId);
            await WaitForProcessedAsync(commandEventId, InventoryReservationRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            // Orders side: saga advanced to WaitingForInventory; the command outbox reached Published.
            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Equal(OrderSagaState.WaitingForInventory, saga!.State);
            Assert.Equal(correlationId, saga.CorrelationId);

            var outbox = await GetOutboxAsync(commandEventId);
            Assert.NotNull(outbox);
            Assert.Equal(OutboxStatus.Published, outbox!.Status);
            Assert.Equal(InventoryReservationRequestedIntegrationEvent.EventType, outbox.Type);
            Assert.Equal(correlationId, outbox.CorrelationId);

            // Inventory side: the reservation was created atomically and idempotently.
            var reservation = await GetReservationAsync(orderId);
            Assert.NotNull(reservation);
            Assert.Equal(sku, reservation!.Sku.Value);
            Assert.Equal(quantity, reservation.Quantity);
            Assert.Equal(InventoryReservationStatus.Pending, reservation.Status);

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(quantity, item.ReservedQuantity);
            Assert.Equal(10 - quantity, item.AvailableQuantity);
            Assert.Equal(1, await CountProcessedAsync(commandEventId, InventoryReservationRequestedConsumer.StableHandlerName));

            // The queue drained and nothing was dead-lettered.
            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, commandEventId, sku);
        }
    }

    [Fact]
    public async Task Saga_RedeliveredOrderCreated_DoesNotReissueCommands_SoExactlyOneReservation()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sku = UniqueSku("8.4-REDELIV").ToUpperInvariant();
        const int quantity = 2;
        var correlationId = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        await _fixture.SeedItemAsync(sku, 10);
        await _fixture.SeedOrderAsync(orderId, customerId, sku, quantity);
        await using var provider = _fixture.BuildProvider(queueName);

        var commandEventId = Guid.Empty;
        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // First OrderCreated delivery: the saga is created and the command staged.
            using (var scope = provider.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
                var first = await orchestrator.StartFromOrderCreatedAsync(
                    new OrdersOrderId(orderId), correlationId, causationId: null, occurredAtUtc);

                Assert.True(first.SagaCreated);
                commandEventId = Assert.Single(first.Commands).EventId;
                await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();
            }

            // Redelivered OrderCreated: the saga already exists, so no new command is staged.
            using (var scope = provider.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
                var redelivery = await orchestrator.StartFromOrderCreatedAsync(
                    new OrdersOrderId(orderId), correlationId, causationId: null, occurredAtUtc);

                Assert.False(redelivery.SagaCreated);
                Assert.Empty(redelivery.Commands);
                Assert.Equal(1, await CountOutboxAsync(commandEventId));
            }

            // Dispatch: exactly one reservation request reaches the broker.
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            await WaitForReservationAsync(orderId);
            await WaitForProcessedAsync(commandEventId, InventoryReservationRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountReservationsAsync(orderId));
            Assert.Equal(1, await CountProcessedAsync(commandEventId, InventoryReservationRequestedConsumer.StableHandlerName));

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(quantity, item.ReservedQuantity);
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, commandEventId, sku);
        }
    }

    private static async Task<RabbitMQConsumerBackgroundService> StartConsumerAsync(ServiceProvider provider)
    {
        var consumer = provider.GetServices<IHostedService>()
            .OfType<RabbitMQConsumerBackgroundService>()
            .Single();
        await consumer.StartAsync(CancellationToken.None);
        return consumer;
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

    private async Task WaitForReservationAsync(Guid orderId)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountReservationsAsync(orderId) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Reservation for order '{orderId}' was not persisted within {Timeout}.");
    }

    private async Task WaitForProcessedAsync(Guid eventId, string handler)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountProcessedAsync(eventId, handler) > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Event {eventId} was not processed by '{handler}' within {Timeout}.");
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

    private async Task<OrderSaga?> GetSagaAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OrderSagas
            .AsNoTracking()
            .FirstOrDefaultAsync(saga => saga.OrderId == new OrdersOrderId(orderId));
    }

    private async Task<OutboxMessage?> GetOutboxAsync(Guid eventId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(message => message.EventId == eventId);
    }

    private async Task<int> CountOutboxAsync(Guid eventId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OutboxMessages.CountAsync(message => message.EventId == eventId);
    }

    private async Task<InventoryReservation?> GetReservationAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(reservation => reservation.OrderId == new InventoryOrderId(orderId));
    }

    private async Task<InventoryItem?> GetItemAsync(string sku)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryItems
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == new InventorySku(sku));
    }

    private async Task<int> CountReservationsAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryReservations.CountAsync(reservation => reservation.OrderId == new InventoryOrderId(orderId));
    }

    private async Task<int> CountProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
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

    private async Task CleanupAsync(
        IServiceProvider provider,
        string queueName,
        Guid orderId,
        Guid commandEventId,
        string sku)
    {
        var ordersOrderId = new OrdersOrderId(orderId);
        var inventoryOrderId = new InventoryOrderId(orderId);

        using (var ordersScope = _fixture.Services.CreateScope())
        {
            var context = ordersScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await context.OrderSagas
                .Where(saga => saga.OrderId == ordersOrderId)
                .ExecuteDeleteAsync();
            if (commandEventId != Guid.Empty)
                await context.OutboxMessages
                    .Where(message => message.EventId == commandEventId)
                    .ExecuteDeleteAsync();
        }

        using (var inventoryScope = _fixture.Services.CreateScope())
        {
            var context = inventoryScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await context.InventoryReservations
                .Where(reservation => reservation.OrderId == inventoryOrderId)
                .ExecuteDeleteAsync();
            if (commandEventId != Guid.Empty)
                await context.ProcessedMessages
                    .Where(message => message.Key == commandEventId.ToString())
                    .ExecuteDeleteAsync();
            await context.InventoryItems
                .Where(item => item.Id == new InventorySku(sku))
                .ExecuteDeleteAsync();
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static string UniqueQueueName() => $"it.sagaflow.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
}