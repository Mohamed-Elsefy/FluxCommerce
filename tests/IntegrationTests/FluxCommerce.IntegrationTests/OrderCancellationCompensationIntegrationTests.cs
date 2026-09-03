using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
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
/// Phase 8.7 verification: the complete compensation flow against the real SQL Server engines and the
/// real RabbitMQ broker (plan §12):
///
///   Order Saga → Reserve → Inventory Reserved → Saga Completed (reservation recorded)
///   → Order Cancelled → Saga Release Request → Inventory Consumer → Reservation Released
///
/// The order is placed and the reservation is secured through the real chain; then an
/// <c>orders.order_cancelled</c> event is published and the Orders compensation consumer issues one
/// <c>inventory.release_requested</c> per recorded reservation through the saga orchestrator; the real
/// Inventory release consumer releases the reservation through the Inventory idempotent-processing
/// boundary and stages <c>inventory.released</c>. The reserved quantity returns to available stock.
/// </summary>
[Collection(OrderSagaInventoryFlowTestCollection.Name)]
public class OrderCancellationCompensationIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly OrderSagaInventoryFlowTestFixture _fixture;

    public OrderCancellationCompensationIntegrationTests(OrderSagaInventoryFlowTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CancelledOrder_ReleasesReservedInventory_EndToEnd()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sku = UniqueSku("8.7-COMP").ToUpperInvariant();
        const int quantity = 3;
        var occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var cancellationCorrelationId = Guid.NewGuid();

        await _fixture.SeedItemAsync(sku, 10);
        await _fixture.SeedOrderAsync(orderId, customerId, sku, quantity);
        await using var provider = _fixture.BuildCompensationProvider(queueName);

        var reservationCommandEventId = Guid.Empty;
        var reservedEventId = Guid.Empty;
        var orderCancelledEventId = Guid.Empty;
        var releaseCommandEventId = Guid.Empty;
        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // 1. The saga orchestrator stages the saga + reservation-request outbox record (the work the
            // OrderCreated saga consumer performs inside the Orders idempotency boundary).
            using (var scope = provider.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
                var result = await orchestrator.StartFromOrderCreatedAsync(
                    new OrdersOrderId(orderId),
                    Guid.NewGuid(),
                    causationId: null,
                    occurredAtUtc);

                Assert.True(result.SagaCreated);
                reservationCommandEventId = Assert.Single(result.Commands).EventId;
                await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();
            }

            // 2. The Orders outbox dispatcher publishes the reservation request; the Inventory consumer
            // reserves and stages inventory.reserved.
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            await WaitForProcessedAsync(reservationCommandEventId, InventoryReservationRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);

            var reservedOutbox = await GetInventoryOutboxByReservationCommandAsync(reservationCommandEventId);
            var reserved = Assert.Single(reservedOutbox);
            Assert.Equal(InventoryReservedIntegrationEvent.EventType, reserved.Type);
            reservedEventId = reserved.EventId;
            var reservationId = JsonSerializer
                .Deserialize<InventoryReservedIntegrationEvent>(reserved.Payload)!
                .ReservationId;

            // 3. The Inventory outbox dispatcher publishes inventory.reserved; the Orders result consumer
            // applies it — the saga records the reservation (advancing to WaitingForPayment) for later
            // release.
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<InventoryOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            await WaitForOrdersProcessedAsync(reservedEventId, InventoryReservedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);

            var completedSaga = await GetSagaAsync(orderId);
            Assert.NotNull(completedSaga);
            Assert.Equal(OrderSagaState.WaitingForPayment, completedSaga!.State);
            Assert.Equal(reservationId, Assert.Single(completedSaga.ReservationIds));

            // 4. The order is cancelled: publish orders.order_cancelled. The compensation consumer issues
            // the release request for the recorded reservation (atomic with the Orders ProcessedMessage).
            var orderCancelled = new OrderCancelledIntegrationEvent(
                orderId,
                customerId,
                "ORD-COMP",
                DateTimeOffset.UtcNow)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = cancellationCorrelationId
            };
            orderCancelledEventId = orderCancelled.EventId;
            var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
            Assert.True(await publisher.PublishAsync(OrderCancelledIntegrationEvent.EventType, orderCancelled));

            await WaitForOrdersProcessedAsync(orderCancelled.EventId, OrderCancelledSagaConsumer.StableHandlerName);

            var releaseCommand = await GetOrdersOutboxByCorrelationAsync(cancellationCorrelationId);
            var release = Assert.Single(releaseCommand);
            Assert.Equal(InventoryReleaseRequestedIntegrationEvent.EventType, release.Type);
            Assert.Equal(cancellationCorrelationId, release.CorrelationId);
            Assert.Equal(orderCancelled.EventId, release.CausationId);
            releaseCommandEventId = release.EventId;

            // 5. The Orders outbox dispatcher publishes the release request; the Inventory release
            // consumer releases the reservation and stages inventory.released.
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<OrdersOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            await WaitForProcessedAsync(releaseCommandEventId, InventoryReleaseRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);

            // 6. The Inventory outbox dispatcher publishes inventory.released (fire-and-forget
            // observability; no consumer is registered for it).
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<InventoryOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            await consumer.StopAsync(CancellationToken.None);

            // Compensation outcome: the reservation is released and the quantity returned to stock.
            var reservation = await GetReservationAsync(orderId);
            Assert.NotNull(reservation);
            Assert.Equal(InventoryReservationStatus.Released, reservation!.Status);

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(0, item.ReservedQuantity);
            Assert.Equal(10, item.AvailableQuantity);

            // The saga is untouched by the compensation (no state transition, no extra release) — it remains
            // WaitingForPayment awaiting the payment leg.
            var finalSaga = await GetSagaAsync(orderId);
            Assert.NotNull(finalSaga);
            Assert.Equal(OrderSagaState.WaitingForPayment, finalSaga!.State);
            Assert.Equal(reservationId, Assert.Single(finalSaga.ReservationIds));

            // The released outbox record carries the workflow correlation and causal link, and the
            // Inventory dispatcher publishes it (fire-and-forget observability).
            var releasedOutbox = await GetInventoryOutboxByCorrelationAsync(cancellationCorrelationId);
            var released = Assert.Single(releasedOutbox);
            Assert.Equal(InventoryReleasedIntegrationEvent.EventType, released.Type);
            Assert.Equal(cancellationCorrelationId, released.CorrelationId);
            Assert.Equal(releaseCommandEventId, released.CausationId);
            Assert.Equal(OutboxStatus.Published, released.Status);
            Assert.NotNull(released.ProcessedAtUtc);

            // Exactly one release command was issued and nothing was dead-lettered.
            Assert.Equal(1, await CountOrdersProcessedAsync(orderCancelled.EventId, OrderCancelledSagaConsumer.StableHandlerName));
            Assert.Equal(1, await CountProcessedAsync(releaseCommandEventId, InventoryReleaseRequestedConsumer.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(
                provider,
                queueName,
                orderId,
                sku,
                [reservationCommandEventId, reservedEventId, orderCancelledEventId, releaseCommandEventId],
                [cancellationCorrelationId]);
        }
    }

    [Fact]
    public async Task CancelledOrder_WithNoRecordedReservation_IssuesNoRelease()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sku = UniqueSku("8.7-NORELEASE").ToUpperInvariant();

        await _fixture.SeedItemAsync(sku, 10);
        await _fixture.SeedOrderAsync(orderId, customerId, sku, 2);
        await using var provider = _fixture.BuildCompensationProvider(queueName);

        var cancellationCorrelationId = Guid.NewGuid();
        var startCorrelationId = Guid.NewGuid();

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // A saga exists but no reservation was recorded (e.g. the saga was started and then the
            // workflow failed before any reservation succeeded).
            using (var scope = provider.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
                var result = await orchestrator.StartFromOrderCreatedAsync(
                    new OrdersOrderId(orderId),
                    startCorrelationId,
                    causationId: null,
                    DateTimeOffset.UtcNow.AddMinutes(-5));

                Assert.True(result.SagaCreated);
                await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();
            }

            var orderCancelled = new OrderCancelledIntegrationEvent(
                orderId,
                customerId,
                "ORD-COMP",
                DateTimeOffset.UtcNow)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = cancellationCorrelationId
            };
            var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
            Assert.True(await publisher.PublishAsync(OrderCancelledIntegrationEvent.EventType, orderCancelled));

            await WaitForOrdersProcessedAsync(orderCancelled.EventId, OrderCancelledSagaConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            // The compensation is a benign no-op: no release command was staged, nothing dead-lettered.
            Assert.Equal(0, await CountOrdersOutboxByCorrelationAsync(cancellationCorrelationId));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Empty(saga!.ReservationIds);
        }
        finally
        {
            await CleanupAsync(
                provider,
                queueName,
                orderId,
                sku,
                [],
                [startCorrelationId, cancellationCorrelationId]);
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

    private async Task WaitForOrdersProcessedAsync(Guid eventId, string handler)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountOrdersProcessedAsync(eventId, handler) > 0)
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

    private async Task<int> CountProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
    }

    private async Task<int> CountOrdersProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
    }

    private async Task<IReadOnlyList<OutboxMessage>> GetOrdersOutboxByCorrelationAsync(Guid correlationId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.CorrelationId == correlationId)
            .ToListAsync();
    }

    private async Task<int> CountOrdersOutboxByCorrelationAsync(Guid correlationId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OutboxMessages.CountAsync(message => message.CorrelationId == correlationId);
    }

    private async Task<IReadOnlyList<OutboxMessage>> GetInventoryOutboxByReservationCommandAsync(Guid eventId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.CausationId == eventId)
            .ToListAsync();
    }

    private async Task<IReadOnlyList<OutboxMessage>> GetInventoryOutboxByCorrelationAsync(Guid correlationId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.CorrelationId == correlationId)
            .ToListAsync();
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
        string sku,
        IReadOnlyCollection<Guid> eventIds,
        IEnumerable<Guid> correlations)
    {
        var ordersOrderId = new OrdersOrderId(orderId);
        var inventoryOrderId = new InventoryOrderId(orderId);
        var keyList = eventIds.Where(id => id != Guid.Empty).Select(id => id.ToString()).ToList();
        var correlationList = correlations.Where(id => id != Guid.Empty).ToList();

        using (var ordersScope = _fixture.Services.CreateScope())
        {
            var context = ordersScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await context.OrderSagas
                .Where(saga => saga.OrderId == ordersOrderId)
                .ExecuteDeleteAsync();
            await context.Orders
                .Where(order => order.Id == ordersOrderId)
                .ExecuteDeleteAsync();
            if (keyList.Count > 0)
                await context.ProcessedMessages
                    .Where(message => keyList.Contains(message.Key))
                    .ExecuteDeleteAsync();
            if (correlationList.Count > 0)
                await context.OutboxMessages
                    .Where(message => correlationList.Contains(message.CorrelationId))
                    .ExecuteDeleteAsync();
        }

        using (var inventoryScope = _fixture.Services.CreateScope())
        {
            var context = inventoryScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await context.InventoryReservations
                .Where(reservation => reservation.OrderId == inventoryOrderId)
                .ExecuteDeleteAsync();
            if (keyList.Count > 0)
                await context.ProcessedMessages
                    .Where(message => keyList.Contains(message.Key))
                    .ExecuteDeleteAsync();
            if (correlationList.Count > 0)
                await context.OutboxMessages
                    .Where(message => correlationList.Contains(message.CorrelationId))
                    .ExecuteDeleteAsync();
            await context.InventoryItems
                .Where(item => item.Id == new InventorySku(sku))
                .ExecuteDeleteAsync();
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static string UniqueQueueName() => $"it.compensation.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
}