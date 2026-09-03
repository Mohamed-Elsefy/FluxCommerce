using System.Globalization;
using System.Text;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
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
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 8.5/8.6: the Orders consumers for the Inventory result events against the real broker and real
/// SQL Server. Verifies the <c>InventoryReservedConsumer</c> and <c>InventoryReservationFailedConsumer</c>
/// end to end: delivery → deserialize → Orders idempotent-processing boundary (ProcessedMessage + saga
/// state commit atomically) → ACK. A success advances the saga to <c>WaitingForPayment</c> (reservation
/// recorded) while the Order stays Pending; a failure applies <c>Failed</c> and cancels the Order. Duplicate
/// deliveries produce exactly one processed-message record and one transition, and nothing is dead-lettered.
/// </summary>
[Collection(RabbitMQOrdersConsumerTestCollection.Name)]
public class OrderResultConsumerIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly RabbitMQOrdersConsumerTestFixture _fixture;

    public OrderResultConsumerIntegrationTests(RabbitMQOrdersConsumerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReservedConsumer_ReceivesReserved_CompletesSaga_AndAcknowledges()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid());
        var correlationId = await SeedSagaAsync(orderId);
        var @event = CreateReservedEvent(orderId);
        await using var provider = _fixture.BuildResultConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName));
            Assert.Equal(OrderSagaState.WaitingForPayment, (await GetSagaAsync(orderId))!.State);
            Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderId))!.Status);

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, correlationId, [@event.EventId]);
        }
    }

    [Fact]
    public async Task FailedConsumer_ReceivesReservationFailed_FailsSaga_CancelsOrder_AndAcknowledges()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid());
        var correlationId = await SeedSagaAsync(orderId);
        var @event = CreateFailedEvent(orderId);
        await using var provider = _fixture.BuildResultConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForProcessedAsync(@event.EventId, InventoryReservationFailedConsumer.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryReservationFailedConsumer.StableHandlerName));
            Assert.Equal(OrderSagaState.Failed, (await GetSagaAsync(orderId))!.State);
            Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderId))!.Status);

            using (var scope = _fixture.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                Assert.Equal(1, await context.OutboxMessages.CountAsync(
                    message => message.Type == OrderCancelledIntegrationEvent.EventType
                        && message.Payload.Contains(orderId.ToString())));
            }

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, correlationId, [@event.EventId]);
        }
    }

    [Fact]
    public async Task ReservedConsumer_DuplicateDelivery_ProcessesExactlyOnce()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid());
        var correlationId = await SeedSagaAsync(orderId);
        var @event = CreateReservedEvent(orderId);
        await using var provider = _fixture.BuildResultConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);
            await PublishAsync(provider, @event);

            await WaitForProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName));
            Assert.Equal(OrderSagaState.WaitingForPayment, (await GetSagaAsync(orderId))!.State);
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, correlationId, [@event.EventId]);
        }
    }

    [Fact]
    public async Task FailedConsumer_DuplicateDelivery_ProcessesExactlyOnce()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid());
        var correlationId = await SeedSagaAsync(orderId);
        var @event = CreateFailedEvent(orderId);
        await using var provider = _fixture.BuildResultConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);
            await PublishAsync(provider, @event);

            await WaitForProcessedAsync(@event.EventId, InventoryReservationFailedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryReservationFailedConsumer.StableHandlerName));
            Assert.Equal(OrderSagaState.Failed, (await GetSagaAsync(orderId))!.State);
            Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderId))!.Status);
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, correlationId, [@event.EventId]);
        }
    }

    [Fact]
    public async Task ReservedConsumer_ResultForUnknownSaga_IsRetriedAndDeadLettered()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, Guid.NewGuid());
        var @event = CreateReservedEvent(orderId);
        var correlationId = @event.CorrelationId;
        await using var provider = _fixture.BuildResultConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // No saga exists for the order: the reserved result cannot be applied without a business
            // effect, so the consumer throws, the infrastructure retries with bounded backoff, and the
            // delivery is dead-lettered as exhausted — the result is never silently lost.
            await PublishAsync(provider, @event);

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(0, await CountProcessedAsync(@event.EventId, InventoryReservedConsumer.StableHandlerName));
            Assert.Null(await GetSagaAsync(orderId));
            Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderId))!.Status);

            var (body, headers) = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.NotNull(body);
            var bodyText = Encoding.UTF8.GetString(body);
            Assert.True(
                bodyText.Contains(orderId.ToString()),
                $"DLQ body ({bodyText.Length} chars) did not contain '{orderId}'. Body: {bodyText}");
            Assert.Equal(
                IntegrationEventMessageProcessor.ExhaustedReason,
                HeaderString(headers[IntegrationEventMessageProcessor.DeadLetterReasonHeader]));
            Assert.True(
                HeaderString(headers[IntegrationEventMessageProcessor.DeadLetterErrorMessageHeader])
                    .Contains("no saga exists"),
                "DLQ x-error-message did not indicate the missing saga.");
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, correlationId, [@event.EventId]);
        }
    }

    private async Task<Guid> SeedSagaAsync(Guid orderId)
    {
        var correlationId = Guid.NewGuid();

        using var scope = _fixture.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>();
        var result = await orchestrator.StartFromOrderCreatedAsync(
            new OrdersOrderId(orderId),
            correlationId,
            causationId: null,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.True(result.SagaCreated);
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync();

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

    private static async Task PublishAsync(IServiceProvider provider, InventoryReservedIntegrationEvent @event) =>
        await PublishAsync(provider, @event, InventoryReservedIntegrationEvent.EventType);

    private static async Task PublishAsync(IServiceProvider provider, InventoryReservationFailedIntegrationEvent @event) =>
        await PublishAsync(provider, @event, InventoryReservationFailedIntegrationEvent.EventType);

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

    private static Task AckSettleAsync() => Task.Delay(300);

    private async Task<int> CountProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.ProcessedMessages.CountAsync(
            message => message.Key == eventId.ToString() && message.Handler == handler);
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

    private static async Task WaitForMessageCountAsync(IServiceProvider provider, string queueName, uint expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await GetMessageCountAsync(provider, queueName) == expected)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Queue '{queueName}' did not reach {expected} message(s) within {Timeout}.");
    }

    private static async Task<(byte[] Body, IDictionary<string, object?> Headers)> GetSingleDeadLetteredMessageAsync(
        IServiceProvider provider,
        string queueName)
    {
        var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
        await using var channel = await connectionProvider.CreateChannelAsync();
        var result = await channel.BasicGetAsync(
            queue: DeadLetterQueueName(queueName),
            autoAck: true,
            cancellationToken: default);
        Assert.NotNull(result);
        return (result.Body.ToArray(), result.BasicProperties.Headers!);
    }

    private static string HeaderString(object? value) => value switch
    {
        string text => text,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        null => string.Empty,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

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
        Guid correlationId,
        IEnumerable<Guid> eventIds)
    {
        var keyList = eventIds.Select(id => id.ToString()).ToList();
        var ordersOrderId = new OrdersOrderId(orderId);

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
            await context.OutboxMessages
                .Where(message => message.CorrelationId == correlationId)
                .ExecuteDeleteAsync();
            await context.OutboxMessages
                .Where(message => message.Type == OrderCancelledIntegrationEvent.EventType
                    && message.Payload.Contains(orderId.ToString()))
                .ExecuteDeleteAsync();
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static string UniqueQueueName() => $"it.result.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static InventoryReservedIntegrationEvent CreateReservedEvent(Guid orderId) =>
        new(
            Guid.NewGuid(),
            orderId,
            "SKU-RESULT",
            3,
            DateTimeOffset.UtcNow.AddMinutes(30),
            InventoryReservationStatus.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1)
        {
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid()
        };

    private static InventoryReservationFailedIntegrationEvent CreateFailedEvent(Guid orderId) =>
        new(orderId, "SKU-RESULT", 3, "Inventory.InsufficientStock")
        {
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid()
        };
}