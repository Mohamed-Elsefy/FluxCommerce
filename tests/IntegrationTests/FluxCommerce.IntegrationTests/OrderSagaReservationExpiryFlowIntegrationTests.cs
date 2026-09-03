using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Application.Expiration;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;
using DomainReservationStatus = FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus;
using InventoryOrderId = FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId;
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 11.10 verification of the reservation-expiry chain against the real SQL Server engines and the
/// real RabbitMQ broker:
///
///   Inventory → Reservation Expiration → Outbox → RabbitMQ → Orders Consumer → Saga → Order State
///
/// The Inventory reservation-expiration processor runs against the real inventory database (expiring the
/// reservation, releasing the stock exactly once, raising the <c>InventoryReservationExpired</c> domain
/// event and staging the <c>inventory.reservation_expired</c> outbox record atomically). The Inventory
/// outbox dispatcher publishes the event to the real broker; the Orders reservation-expiry consumer
/// (inside the Orders idempotent-processing boundary) applies <c>WaitingForPayment → Cancelled</c> and
/// cancels the Order. Duplicate delivery (Scenario 2) and concurrent consumers (Scenario 3) are also
/// verified against the real broker: one business effect only, nothing dead-lettered.
/// </summary>
[Collection(OrderSagaInventoryFlowTestCollection.Name)]
public class OrderSagaReservationExpiryFlowIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly OrderSagaInventoryFlowTestFixture _fixture;

    public OrderSagaReservationExpiryFlowIntegrationTests(OrderSagaInventoryFlowTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReservationExpiry_FullChain_ExpiresInInventory_PublishesToBroker_OrdersConsumerCancelsSagaAndOrder_EndToEnd()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sku = UniqueSku("11.10-EXP").ToUpperInvariant();
        const int quantity = 3;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Inventory: an item holding a Pending reservation that is already past its deadline.
        var reservationId = await _fixture.SeedReservationAsync(sku, orderId, quantity, expiresAt, onHand: 10);

        // Orders: a Pending order with its saga advanced exactly to WaitingForPayment.
        await _fixture.SeedOrderAsync(orderId, customerId, sku, quantity);
        var startCorrelationId = await SeedWaitingForPaymentSagaAsync(orderId);

        await using var provider = _fixture.BuildExpiryProvider(queueName);

        Guid expiredEventId = Guid.Empty;
        Guid expiredCorrelationId = Guid.Empty;
        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // 1. The real Inventory expiration processor expires the reservation against the real SQL
            //    Server engine: the reservation becomes terminal (Expired), the stock is released once,
            //    and the inventory.reservation_expired outbox record is staged atomically.
            using (var scope = provider.CreateScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<IExpiredReservationProcessor>();
                var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);
                Assert.Equal(1, expired);
            }

            var itemAfterExpiry = await GetItemAsync(sku);
            Assert.NotNull(itemAfterExpiry);
            Assert.Equal(0, itemAfterExpiry!.ReservedQuantity);
            Assert.Equal(10, itemAfterExpiry.AvailableQuantity);
            var reservationAfterExpiry = await GetReservationAsync(orderId);
            Assert.NotNull(reservationAfterExpiry);
            Assert.Equal(DomainReservationStatus.Expired, reservationAfterExpiry!.Status);

            // The staged outbox carries the producing domain-event id and the inventory-side correlation.
            var expiredOutbox = await GetInventoryExpiredOutboxAsync(orderId);
            var expiredMessage = Assert.Single(expiredOutbox);
            expiredEventId = expiredMessage.EventId;
            expiredCorrelationId = expiredMessage.CorrelationId;
            Assert.Equal(InventoryReservationExpiredIntegrationEvent.EventType, expiredMessage.Type);
            Assert.NotEqual(Guid.Empty, expiredCorrelationId);

            // 2. The Inventory outbox dispatcher publishes the event to the real RabbitMQ broker.
            using (var dispatcherScope = provider.CreateScope())
            {
                var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<InventoryOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);
            }

            await WaitForInventoryPublishedAsync(expiredEventId);

            // 3. The Orders reservation-expiry consumer receives it through the Orders idempotent
            //    boundary and applies WaitingForPayment → Cancelled, cancelling the Order.
            await WaitForOrdersProcessedAsync(expiredEventId, InventoryReservationExpiredConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountOrdersProcessedAsync(expiredEventId, InventoryReservationExpiredConsumer.StableHandlerName));

            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Equal(OrderSagaState.Cancelled, saga!.State);

            var order = await GetOrderAsync(orderId);
            Assert.NotNull(order);
            Assert.Equal(OrderStatus.Cancelled, order!.Status);

            Assert.Equal(0, await CountOrdersProcessedAsync(Guid.NewGuid(), InventoryReservationExpiredConsumer.StableHandlerName));

            // Scenario 10: released exactly once in Inventory at expiry time — the saga must not re-issue
            // an inventory.release_requested (that would double-release). The atomic side effect of the
            // expiry transition is a single orders.order_cancelled outbox record (mapped by the Orders
            // outbox factory, which stamps its own correlation id).
            var cancellationCommandFound = false;
            using (var scope = _fixture.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                var cancelled = Assert.Single(await context.OutboxMessages
                    .AsNoTracking()
                    .Where(message =>
                        message.Type == OrderCancelledIntegrationEvent.EventType
                        && message.Payload.Contains(orderId.ToString()))
                    .ToListAsync());
                Assert.Equal(OrderCancelledIntegrationEvent.EventType, cancelled.Type);
                Assert.NotEqual(Guid.Empty, cancelled.CorrelationId);
                cancellationCommandFound = true;
            }
            Assert.True(cancellationCommandFound);

            // No re-release command is staged for the expired reservation.
            Assert.Empty(await GetOrdersOutboxByTypeAsync(InventoryReleaseRequestedIntegrationEvent.EventType));

            // Nothing was dead-lettered.
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(
                provider,
                queueName,
                orderId,
                sku,
                correlations: [startCorrelationId, expiredCorrelationId],
                eventIds: expiredEventId == Guid.Empty ? [] : [expiredEventId]);
        }
    }

    [Fact]
    public async Task ReservationExpiry_DuplicateDelivery_ProducesSingleBusinessEffect()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sku = UniqueSku("11.10-DUP").ToUpperInvariant();
        const int quantity = 2;

        await _fixture.SeedOrderAsync(orderId, customerId, sku, quantity);
        var startCorrelationId = await SeedWaitingForPaymentSagaAsync(orderId);

        var reservationId = Guid.NewGuid();
        var expiredCorrelationId = Guid.NewGuid();
        var expiredEvent = new InventoryReservationExpiredIntegrationEvent(
            reservationId,
            orderId,
            sku,
            quantity,
            DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            EventId = Guid.NewGuid(),
            CorrelationId = expiredCorrelationId
        };

        await using var provider = _fixture.BuildExpiryProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // The identical event (same EventId + correlation) is delivered twice - as happens with an
            // at-least-once redelivery. The Orders idempotent-processing boundary acknowledges the second
            // delivery without re-executing the business operation.
            await PublishAsync(provider, expiredEvent);
            await PublishAsync(provider, expiredEvent);

            await WaitForOrdersProcessedAsync(expiredEvent.EventId, InventoryReservationExpiredConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountOrdersProcessedAsync(expiredEvent.EventId, InventoryReservationExpiredConsumer.StableHandlerName));

            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Equal(OrderSagaState.Cancelled, saga!.State);
            Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderId))!.Status);

            // One business effect: exactly one cancellation outbox record.
            using (var scope = _fixture.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                Assert.Equal(1, await context.OutboxMessages.CountAsync(message =>
                    message.Type == OrderCancelledIntegrationEvent.EventType
                    && message.Payload.Contains(orderId.ToString())));
            }

            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(
                provider,
                queueName,
                orderId,
                sku,
                correlations: [startCorrelationId, expiredCorrelationId],
                eventIds: [expiredEvent.EventId]);
        }
    }

    [Fact]
    public async Task ReservationExpiry_ConcurrentConsumers_ProduceSingleSagaTransition_AndNothingIsDeadLettered()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var skuA = UniqueSku("11.10-CA").ToUpperInvariant();
        var skuB = UniqueSku("11.10-CB").ToUpperInvariant();
        const int quantity = 1;

        await _fixture.SeedOrderAsync(orderId, customerId, skuA, quantity);
        var startCorrelationId = await SeedWaitingForPaymentSagaAsync(orderId);

        // Two distinct expired reservations for the same order, delivered to two concurrent consumers.
        var reservationIdA = await _fixture.SeedReservationAsync(skuA, orderId, quantity, DateTimeOffset.UtcNow.AddMinutes(-5), onHand: 5);
        var reservationIdB = await _fixture.SeedReservationAsync(skuB, orderId, quantity, DateTimeOffset.UtcNow.AddMinutes(-5), onHand: 5);

        var eventA = CreateExpiredEvent(reservationIdA, orderId, skuA, quantity);
        var eventB = CreateExpiredEvent(reservationIdB, orderId, skuB, quantity);

        await using var provider = _fixture.BuildExpiryProvider(queueName, options => options.Concurrency = 2);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, eventA);
            await PublishAsync(provider, eventB);

            await WaitForOrdersProcessedAsync(eventA.EventId, InventoryReservationExpiredConsumer.StableHandlerName);
            await WaitForOrdersProcessedAsync(eventB.EventId, InventoryReservationExpiredConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountOrdersProcessedAsync(eventA.EventId, InventoryReservationExpiredConsumer.StableHandlerName));
            Assert.Equal(1, await CountOrdersProcessedAsync(eventB.EventId, InventoryReservationExpiredConsumer.StableHandlerName));

            // The saga version token serializes the transition: exactly one expiry applies the
            // WaitingForPayment → Cancelled transition; the other is acknowledged as a safe ignore.
            var saga = await GetSagaAsync(orderId);
            Assert.NotNull(saga);
            Assert.Equal(OrderSagaState.Cancelled, saga!.State);

            Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderId))!.Status);

            using (var scope = _fixture.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                Assert.Equal(1, await context.OutboxMessages.CountAsync(message =>
                    message.Type == OrderCancelledIntegrationEvent.EventType
                    && message.Payload.Contains(orderId.ToString())));
            }

            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(
                provider,
                queueName,
                orderId,
                skuA,
                correlations: [startCorrelationId, eventA.CorrelationId, eventB.CorrelationId],
                eventIds: [eventA.EventId, eventB.EventId]);
            await DeleteSkuAsync(skuB);
        }
    }

    private async Task<Guid> SeedWaitingForPaymentSagaAsync(Guid orderId)
    {
        var correlationId = Guid.NewGuid();

        using (var scope = _fixture.Services.CreateScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
            var result = await orchestrator.StartFromOrderCreatedAsync(
                new OrdersOrderId(orderId),
                correlationId,
                causationId: null,
                DateTimeOffset.UtcNow.AddMinutes(-10));
            Assert.True(result.SagaCreated);
            await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();
        }

        using (var scope = _fixture.Services.CreateScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
            var reserved = await orchestrator.ApplyReservationSuccessAsync(
                new OrdersOrderId(orderId),
                Guid.NewGuid(),
                correlationId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(-5));
            Assert.Equal(OrderSagaTransitionOutcome.Applied, reserved.Outcome);
            await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();

            var saga = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().OrderSagas
                .AsNoTracking()
                .SingleAsync(s => s.OrderId == new OrdersOrderId(orderId));
            Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        }

        return correlationId;
    }

    private static async Task<RabbitMQConsumerBackgroundService> StartConsumerAsync(ServiceProvider provider)
    {
        var consumer = provider.GetServices<IHostedService>()
            .OfType<RabbitMQConsumerBackgroundService>()
            .Single();
        await consumer.StartAsync(CancellationToken.None);
        return consumer;
    }

    private static async Task PublishAsync(IServiceProvider provider, IIntegrationEvent @event, string eventType)
    {
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var confirmed = await publisher.PublishAsync(eventType, @event);
        Assert.True(confirmed);
    }

    private static async Task PublishAsync(IServiceProvider provider, InventoryReservationExpiredIntegrationEvent @event) =>
        await PublishAsync(provider, @event, InventoryReservationExpiredIntegrationEvent.EventType);

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

    private async Task WaitForInventoryPublishedAsync(Guid eventId)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _fixture.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var published = await context.OutboxMessages
                .AsNoTracking()
                .AnyAsync(message => message.EventId == eventId && message.Status == FluxCommerce.BuildingBlocks.Outbox.Messages.OutboxStatus.Published);
            if (published)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Inventory outbox event {eventId} was not published within {Timeout}.");
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

    private async Task<Order?> GetOrderAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(order => order.Id == new OrdersOrderId(orderId));
    }

    private async Task<InventoryItem?> GetItemAsync(string sku)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryItems
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == new InventorySku(sku));
    }

    private async Task<InventoryReservation?> GetReservationAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(reservation => reservation.OrderId == new InventoryOrderId(orderId));
    }

    private async Task<int> CountOrdersProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
    }

    private async Task<IReadOnlyList<OutboxMessage>> GetInventoryExpiredOutboxAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.Type == InventoryReservationExpiredIntegrationEvent.EventType
                && message.Payload.Contains(orderId.ToString()))
            .ToListAsync();
    }

    private async Task<string> GetInventoryOutboxDiagnosticsAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var rows = await context.OutboxMessages
            .AsNoTracking()
            .Select(message => $"{message.Type}:{message.Status}({message.EventId})")
            .ToListAsync();
        return rows.Count == 0 ? "<none>" : string.Join("; ", rows);
    }

    private async Task<IReadOnlyList<OutboxMessage>> GetOrdersOutboxByTypeAsync(string eventType)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Type == eventType)
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

    private async Task DeleteSkuAsync(string sku)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await context.InventoryItems
            .Where(item => item.Id == new InventorySku(sku))
            .ExecuteDeleteAsync();
    }

    private async Task CleanupAsync(
        IServiceProvider provider,
        string queueName,
        Guid orderId,
        string sku,
        IReadOnlyCollection<Guid> correlations,
        IReadOnlyCollection<Guid> eventIds)
    {
        var keyList = eventIds.Where(id => id != Guid.Empty).Select(id => id.ToString()).ToList();
        var correlationList = correlations.Where(id => id != Guid.Empty).ToList();
        var ordersOrderId = new OrdersOrderId(orderId);
        var inventoryOrderId = new InventoryOrderId(orderId);

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            if (keyList.Count > 0)
                await context.ProcessedMessages
                    .Where(message => keyList.Contains(message.Key))
                    .ExecuteDeleteAsync();
            await context.OrderSagas
                .Where(saga => saga.OrderId == ordersOrderId)
                .ExecuteDeleteAsync();
            await context.Orders
                .Where(order => order.Id == ordersOrderId)
                .ExecuteDeleteAsync();
            if (correlationList.Count > 0)
                await context.OutboxMessages
                    .Where(message => correlationList.Contains(message.CorrelationId))
                    .ExecuteDeleteAsync();
            await context.OutboxMessages
                .Where(message => message.Type == OrderCancelledIntegrationEvent.EventType
                    && message.Payload.Contains(orderId.ToString()))
                .ExecuteDeleteAsync();
        }

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
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

    private static string UniqueQueueName() => $"it.expiry.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];

    private static InventoryReservationExpiredIntegrationEvent CreateExpiredEvent(
        Guid reservationId,
        Guid orderId,
        string sku,
        int quantity) =>
        new(
            reservationId,
            orderId,
            sku,
            quantity,
            DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        };
}