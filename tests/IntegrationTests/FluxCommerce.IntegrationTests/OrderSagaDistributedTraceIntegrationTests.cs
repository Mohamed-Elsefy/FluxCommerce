using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
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
/// Phase 8.9 verification (plan §13): the full Order/Inventory saga workflow runs end to end against the
/// real SQL Server engines and the real RabbitMQ broker while a distributed trace is recorded, and the
/// complete path is reconstructable from the captured spans and the structured logs. One root activity
/// (Checkout) is started around the published <c>orders.order_created</c>; the test asserts that every
/// span created across every async boundary — the saga start/apply saga spans, the outbox dispatch
/// producer spans, and the consumer spans — shares that root trace id, that the correlation/causation
/// chain is intact, that the saga transition logs carry the previous/new state and trace identifiers, and
/// that no business payload is logged.
/// </summary>
[Collection(OrderSagaInventoryFlowTestCollection.Name)]
public class OrderSagaDistributedTraceIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly OrderSagaInventoryFlowTestFixture _fixture;

    public OrderSagaDistributedTraceIntegrationTests(OrderSagaInventoryFlowTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FullWorkflow_ProducesSingleDistributedTrace_ReconstructableFromLogsAndSpans()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sku = UniqueSku("SKU-8.9");
        var correlationId = Guid.NewGuid();

        await _fixture.SeedOrderAsync(orderId, customerId, sku, 3);
        await _fixture.SeedItemAsync(sku, 10);

        var processorLogs = new IntegrationTestCapturingLogger<IntegrationEventMessageProcessor>();
        var orchestratorLogs = new IntegrationTestCapturingLogger<OrderSagaOrchestrator>();

        await using var provider = _fixture.BuildTraceProvider(
            queueName,
            processorLogs,
            orchestratorLogs);

        // The listener samples every FluxCommerce activity process-wide, so assertions filter the
        // captured activities to this test's trace id before checking continuity across the boundaries.
        using var listener = new TestActivityListener();

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // The test's Checkout root activity establishes the trace id that must span every hop.
            var orderCreated = new OrderCreatedIntegrationEvent(
                orderId,
                customerId,
                $"ORD-8.9-{Guid.NewGuid():N}"[..16],
                DateTimeOffset.UtcNow)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = correlationId,
                CausationId = null,
                OccurredAtUtc = DateTime.UtcNow
            };

            ActivityTraceId rootTraceId;
            using (var root = new Activity("Checkout.Test").Start())
            {
                rootTraceId = root.TraceId;
                await PublishAsync(provider, orderCreated);
            }

            // Leg 1: the saga consumer boots the saga and stages the reservation-request outbox record.
            var reservationRequested = await WaitForOrdersOutboxAsync(
                correlationId,
                InventoryReservationRequestedIntegrationEvent.EventType);

            await WaitForSagaAsync(orderId, OrderSagaState.WaitingForInventory);
            Assert.Single(reservationRequested);

            await provider.GetRequiredService<OrdersOutboxDispatcher>()
                .DispatchPendingAsync(CancellationToken.None);

            // Leg 2: the Inventory reservation consumer executes the reservation and stages the result.
            var reserved = await WaitForInventoryOutboxAsync(
                correlationId,
                InventoryReservedIntegrationEvent.EventType);

            Assert.Single(reserved);

            await provider.GetRequiredService<InventoryOutboxDispatcher>()
                .DispatchPendingAsync(CancellationToken.None);

            // Leg 3: the Orders result consumer applies the result and completes the saga.
            await WaitForSagaAsync(orderId, OrderSagaState.Completed);
            await WaitForQueueEmptyAsync(provider, queueName);

            await consumer.StopAsync(CancellationToken.None);

            var reservationRequestedRecord = Assert.Single(reservationRequested);
            var reservedRecord = Assert.Single(reserved);

            // Correlation/causation chain: every hop preserves the Checkout correlation id, and each
            // produced event names the consumed event that caused it.
            Assert.Equal(correlationId, reservationRequestedRecord.CorrelationId);
            Assert.Equal(correlationId, reservedRecord.CorrelationId);
            Assert.Equal(orderCreated.EventId, reservationRequestedRecord.CausationId);
            Assert.Equal(reservationRequestedRecord.EventId, reservedRecord.CausationId);

            // The persisted trace context matches the staging activity on each side.
            Assert.NotNull(reservationRequestedRecord.TraceParent);
            Assert.NotNull(reservedRecord.TraceParent);

            // Every span across every boundary shares the root trace id; the full path is present.
            var traceActivities = listener.Activities
                .Where(activity => activity.TraceId == rootTraceId)
                .ToList();

            Assert.NotEmpty(traceActivities);
            Assert.All(traceActivities, activity => Assert.Equal(rootTraceId, activity.TraceId));

            var spanNames = traceActivities.Select(activity => activity.OperationName).ToList();
            Assert.Contains("Orders.Saga.Start", spanNames);
            Assert.Contains("Orders.Saga.ApplyReservationSuccess", spanNames);
            Assert.Equal(2, spanNames.Count(name => name == "Outbox.Dispatch"));
            Assert.Contains(spanNames, name => name == "Consume orders.order_created");
            Assert.Contains(spanNames, name => name == "Consume inventory.reservation_requested");
            Assert.Contains(spanNames, name => name == "Consume inventory.reserved");

            // The saga transition logs carry the previous/new state and the trace identifiers.
            var createdLog = orchestratorLogs.Entries.SingleOrDefault(entry => entry.Message.Contains("Created saga"));
            Assert.NotNull(createdLog);
            Assert.Equal(OrderSagaState.Started.ToString(), createdLog["PreviousState"]?.ToString());
            Assert.Equal(OrderSagaState.WaitingForInventory.ToString(), createdLog["NewState"]?.ToString());
            Assert.Equal(rootTraceId.ToString(), createdLog["TraceId"]);
            Assert.NotNull(createdLog["SpanId"]);

            var appliedLog = orchestratorLogs.Entries.SingleOrDefault(entry => entry.Message.Contains("Applied inventory.reserved"));
            Assert.NotNull(appliedLog);
            Assert.Equal(OrderSagaState.WaitingForInventory.ToString(), appliedLog["PreviousState"]?.ToString());
            Assert.Equal(OrderSagaState.Completed.ToString(), appliedLog["NewState"]?.ToString());
            Assert.Equal(rootTraceId.ToString(), appliedLog["TraceId"]);
            Assert.NotNull(appliedLog["SpanId"]);

            // The consumer logs carry the correlation identifiers for every hop.
            Assert.Contains(processorLogs.Entries, entry =>
                entry.Message.Contains("processed orders.order_created")
                && string.Equals(entry["CorrelationId"]?.ToString(), correlationId.ToString(), StringComparison.Ordinal));
            Assert.Contains(processorLogs.Entries, entry =>
                entry.Message.Contains("processed inventory.reservation_requested")
                && string.Equals(entry["CorrelationId"]?.ToString(), correlationId.ToString(), StringComparison.Ordinal));
            Assert.Contains(processorLogs.Entries, entry =>
                entry.Message.Contains("processed inventory.reserved")
                && string.Equals(entry["CorrelationId"]?.ToString(), correlationId.ToString(), StringComparison.Ordinal));

            // No business payload is ever logged.
            Assert.DoesNotContain(processorLogs.Entries, entry => entry.Message.Contains("Saga Test Product"));
            Assert.DoesNotContain(orchestratorLogs.Entries, entry => entry.Message.Contains("Saga Test Product"));
            Assert.DoesNotContain(processorLogs.Entries, entry => entry.Message.Contains(sku));
            Assert.DoesNotContain(orchestratorLogs.Entries, entry => entry.Message.Contains(sku));

            // The workflow completed exactly once: one saga, one reservation, nothing dead-lettered.
            var completedSaga = await GetSagaAsync(orderId);
            Assert.Equal(OrderSagaState.Completed, completedSaga!.State);
            Assert.Single(await GetReservationsAsync(orderId));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, [correlationId]);
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

    private static async Task PublishAsync(IServiceProvider provider, IIntegrationEvent @event)
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

    private async Task<IReadOnlyList<OutboxMessage>> WaitForOrdersOutboxAsync(Guid correlationId, string type)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var messages = await GetOrdersOutboxAsync(correlationId, type);
            if (messages.Count > 0)
                return messages;
            await Task.Delay(50);
        }

        Assert.Fail($"No Orders outbox record of type '{type}' for correlation {correlationId} within {Timeout}.");
        return [];
    }

    private async Task<IReadOnlyList<OutboxMessage>> WaitForInventoryOutboxAsync(Guid correlationId, string type)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var messages = await GetInventoryOutboxAsync(correlationId, type);
            if (messages.Count > 0)
                return messages;
            await Task.Delay(50);
        }

        Assert.Fail($"No Inventory outbox record of type '{type}' for correlation {correlationId} within {Timeout}.");
        return [];
    }

    private async Task<OrderSaga> WaitForSagaAsync(Guid orderId, OrderSagaState state)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var saga = await GetSagaAsync(orderId);
            if (saga is not null && saga.State == state)
                return saga;
            await Task.Delay(50);
        }

        Assert.Fail($"Saga for order {orderId} did not reach state {state} within {Timeout}.");
        throw new UnreachableException();
    }

    private async Task<IReadOnlyList<OutboxMessage>> GetOrdersOutboxAsync(Guid correlationId, string type)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.CorrelationId == correlationId && message.Type == type)
            .ToListAsync();
    }

    private async Task<IReadOnlyList<OutboxMessage>> GetInventoryOutboxAsync(Guid correlationId, string type)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.CorrelationId == correlationId && message.Type == type)
            .ToListAsync();
    }

    private async Task<OrderSaga?> GetSagaAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OrderSagas
            .AsNoTracking()
            .FirstOrDefaultAsync(saga => saga.OrderId == new OrdersOrderId(orderId));
    }

    private async Task<IReadOnlyList<InventoryReservation>> GetReservationsAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryReservations
            .AsNoTracking()
            .Where(reservation => reservation.OrderId == new InventoryOrderId(orderId))
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

    private async Task CleanupAsync(
        IServiceProvider provider,
        string queueName,
        Guid orderId,
        IReadOnlyCollection<Guid> correlationIds)
    {
        var correlationList = correlationIds.ToList();

        using (var ordersScope = _fixture.Services.CreateScope())
        {
            var context = ordersScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await context.OrderSagas
                .Where(saga => saga.OrderId == new OrdersOrderId(orderId))
                .ExecuteDeleteAsync();
            await context.Orders
                .Where(order => order.Id == new OrdersOrderId(orderId))
                .ExecuteDeleteAsync();
            if (correlationList.Count > 0)
            {
                await context.OutboxMessages
                    .Where(message => correlationList.Contains(message.CorrelationId))
                    .ExecuteDeleteAsync();
            }
        }

        using (var inventoryScope = _fixture.Services.CreateScope())
        {
            var context = inventoryScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await context.InventoryReservations
                .Where(reservation => reservation.OrderId == new InventoryOrderId(orderId))
                .ExecuteDeleteAsync();
            if (correlationList.Count > 0)
            {
                await context.OutboxMessages
                    .Where(message => correlationList.Contains(message.CorrelationId))
                    .ExecuteDeleteAsync();
            }
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static Task DeleteQueueAsync(IServiceProvider provider, string queueName) =>
        Task.Run(async () =>
        {
            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            try
            {
                await channel.QueueDeleteAsync(
                    queue: DeadLetterQueueName(queueName),
                    ifUnused: false,
                    ifEmpty: false,
                    noWait: false,
                    cancellationToken: default);
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
            }

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

    private static string UniqueQueueName() => $"it.trace.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
}