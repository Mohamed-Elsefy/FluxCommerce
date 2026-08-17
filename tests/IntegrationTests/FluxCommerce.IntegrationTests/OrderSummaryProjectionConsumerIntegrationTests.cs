using System.Globalization;
using System.Text;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 7.9: the first real integration event consumer against the real broker and real SQL Server.
/// Verifies the <c>OrderSummaryProjectionConsumer</c> end to end: delivery → deserialize → Orders
/// idempotent-processing boundary (Business State + ProcessedMessage atomic commit) → ACK. Covered
/// scenarios: happy-path projection persistence with the originating metadata, duplicate delivery with
/// exactly one business effect, a missing source order failing then succeeding through the consumer
/// retry (exactly one effect after the order is seeded), correlation/causation propagation into the
/// projection, and ACK-after-commit (the queue drains and nothing is dead-lettered).
///
/// Phase 7.10 hardening scenarios against the real broker and SQL Server: concurrent duplicate
/// deliveries of the same event through multiple consumer channels converge on exactly one business
/// effect and one ProcessedMessage (the composite (EventId, Handler) primary key is the sole arbiter),
/// a failure before the business commit rolls back atomically and the unacknowledged delivery is
/// redelivered by the broker after a graceful stop and succeeds with exactly one effect, a
/// commit-before-ACK redelivery is handled as a duplicate, retry exhaustion dead-letters with no
/// business effect and enriched failure metadata, and the structured consumer logs expose
/// correlation/causation/consumer/attempt/result for a delivery (observability contract).
/// </summary>
[Collection(RabbitMQOrdersConsumerTestCollection.Name)]
public class OrderSummaryProjectionConsumerIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly RabbitMQOrdersConsumerTestFixture _fixture;

    public OrderSummaryProjectionConsumerIntegrationTests(RabbitMQOrdersConsumerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Consumer_ProjectsOrderSummary_AndAcknowledges()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var order = await _fixture.SeedOrderAsync(orderId, customerId);
        var correlationId = Guid.NewGuid();
        var @event = CreateOrderCreated(orderId, customerId, correlationId, causationId: null);
        await using var provider = _fixture.BuildConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForProjectionAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            var projection = await GetProjectionAsync(orderId);
            Assert.NotNull(projection);
            Assert.Equal(order.OrderNumber.Value, projection!.OrderNumber);
            Assert.Equal(customerId, projection.CustomerId);
            Assert.Equal("Pending", projection.Status);
            Assert.Equal(20.00m, projection.GrandTotalAmount);
            Assert.Equal("USD", projection.GrandTotalCurrency);
            Assert.Equal(1, projection.ItemCount);
            Assert.Equal(correlationId, projection.CorrelationId);
            Assert.Null(projection.CausationId);
            Assert.Equal(order.CreatedAtUtc, projection.CreatedAtUtc);
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task Consumer_DuplicateDelivery_ProducesSingleProjection()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, customerId);
        var @event = CreateOrderCreated(orderId, customerId, Guid.NewGuid(), causationId: null);
        await using var provider = _fixture.BuildConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);
            await PublishAsync(provider, @event);

            await WaitForProjectionAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProjectionsAsync(orderId));
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task Consumer_MissingOrder_FailsThenRetries_AndProjectsAfterOrderSeeded()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var @event = CreateOrderCreated(orderId, customerId, Guid.NewGuid(), causationId: null);
        await using var provider = _fixture.BuildConsumerProvider(
            queueName,
            options =>
            {
                options.Retry.MaxAttempts = 5;
                options.Retry.BackoffType = ConsumerBackoffType.Fixed;
                options.Retry.InitialDelay = TimeSpan.FromMilliseconds(500);
            });

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // The order does not exist yet: the first attempt is a retryable failure. The source order
            // is seeded within the backoff window so a later attempt succeeds.
            await PublishAsync(provider, @event);
            await _fixture.SeedOrderAsync(orderId, customerId);

            await WaitForProjectionAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProjectionsAsync(orderId));
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task Consumer_PreservesCorrelationAndCausation_InProjection()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, customerId);
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var @event = CreateOrderCreated(orderId, customerId, correlationId, causationId);
        await using var provider = _fixture.BuildConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForProjectionAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            var projection = await GetProjectionAsync(orderId);
            Assert.NotNull(projection);
            Assert.Equal(correlationId, projection!.CorrelationId);
            Assert.Equal(causationId, projection.CausationId);
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task Consumer_ConcurrentDuplicateDelivery_ProducesExactlyOneEffect()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, customerId);
        var @event = CreateOrderCreated(orderId, customerId, Guid.NewGuid(), causationId: null);
        await using var provider = _fixture.BuildConsumerProvider(
            queueName,
            options => options.Concurrency = 3);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerCountAsync(provider, queueName, 3);

            // Four identical deliveries are round-robined across the three channels so several are
            // processed concurrently; the (EventId, Handler) composite primary key is the sole arbiter
            // and the losers must converge to a duplicate acknowledgement (never a dead-letter).
            for (var i = 0; i < 4; i++)
                await PublishAsync(provider, @event);

            await WaitForProjectionAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProjectionsAsync(orderId));
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task Consumer_FailureBeforeCommit_RollsBack_ThenRedeliverySucceeds()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var @event = CreateOrderCreated(orderId, customerId, Guid.NewGuid(), causationId: null);
        var logs = new IntegrationTestCapturingLogger<IntegrationEventMessageProcessor>();
        await using var providerA = _fixture.BuildConsumerProvider(
            queueName,
            options =>
            {
                options.Retry.MaxAttempts = 5;
                options.Retry.BackoffType = ConsumerBackoffType.Fixed;
                options.Retry.InitialDelay = TimeSpan.FromSeconds(30);
            },
            logs);

        try
        {
            var consumerA = await StartConsumerAsync(providerA);
            await WaitForConsumerAsync(providerA, queueName);

            // The source order does not exist yet: the first attempt is a retryable business failure.
            await PublishAsync(providerA, @event);

            // Deterministic sync: the structured 'retrying' log is written only after TryProcessAsync
            // threw, i.e. after the atomic transaction rolled back — so no projection and no
            // ProcessedMessage were committed by the failed attempt.
            await WaitForRetryingLogAsync(logs, @event.EventId);
            Assert.Equal(0, await CountProjectionsAsync(orderId));
            Assert.Equal(0, await CountProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));

            // Graceful shutdown cancels the long in-process backoff; the unacknowledged delivery is
            // redelivered by the broker after the channel closes (at-least-once). Because the failed
            // attempt never committed, the redelivery starts from a clean state.
            await consumerA.StopAsync(CancellationToken.None);
            await WaitForMessageCountAsync(providerA, queueName, 1);

            // Seed the source order and consume the redelivery with a fresh host: it must succeed with
            // exactly one projection and one ProcessedMessage — the failed attempt left no trace.
            await _fixture.SeedOrderAsync(orderId, customerId);
            await using var providerB = _fixture.BuildConsumerProvider(queueName);
            var consumerB = await StartConsumerAsync(providerB);
            await WaitForConsumerAsync(providerB, queueName);

            await WaitForProjectionAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(providerB, queueName);
            await consumerB.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProjectionsAsync(orderId));
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(providerB, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(providerA, queueName, [orderId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task Consumer_CommitBeforeAck_RedeliveryIsHandledAsDuplicate()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, customerId);
        var @event = CreateOrderCreated(orderId, customerId, Guid.NewGuid(), causationId: null);
        await using var provider = _fixture.BuildConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // First delivery commits Business State + ProcessedMessage atomically and is ACKed.
            await PublishAsync(provider, @event);
            await WaitForProjectionAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            Assert.Equal(1, await CountProjectionsAsync(orderId));

            // Simulate the broker redelivering the same message after a lost ACK: an identical delivery
            // (same EventId + metadata) must be detected as a duplicate and ACKed without a second effect.
            await PublishAsync(provider, @event);

            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProjectionsAsync(orderId));
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task Consumer_RetryExhaustion_DeadLettersWithoutBusinessEffect()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var @event = CreateOrderCreated(orderId, customerId, Guid.NewGuid(), causationId: null);
        await using var provider = _fixture.BuildConsumerProvider(
            queueName,
            options =>
            {
                options.Retry.MaxAttempts = 2;
                options.Retry.BackoffType = ConsumerBackoffType.Fixed;
                options.Retry.InitialDelay = TimeSpan.FromMilliseconds(100);
            });

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // The source order never exists: both attempts are retryable business failures, then the
            // policy is exhausted and the delivery is dead-lettered — with no business effect and no
            // ProcessedMessage record left behind.
            await PublishAsync(provider, @event);

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(0, await CountProjectionsAsync(orderId));
            Assert.Equal(0, await CountProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName));

            var deadLettered = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.Equal(
                IntegrationEventMessageProcessor.ExhaustedReason,
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterReasonHeader]));
            Assert.Equal(
                2,
                Convert.ToInt32(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterAttemptHeader]));
            Assert.Equal(
                OrderSummaryProjectionConsumer.StableHandlerName,
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterConsumerHeader]));
            Assert.Equal(
                nameof(InvalidOperationException),
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterErrorTypeHeader]));
            Assert.Equal(
                @event.CorrelationId.ToString(),
                HeaderString(deadLettered.Headers["CorrelationId"]));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId]);
        }
    }

    [Fact]
    public async Task Consumer_StructuredLogs_ExposeCorrelationCausationAndOutcome()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _fixture.SeedOrderAsync(orderId, customerId);
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var @event = CreateOrderCreated(orderId, customerId, correlationId, causationId);
        var logs = new IntegrationTestCapturingLogger<IntegrationEventMessageProcessor>();
        await using var provider = _fixture.BuildConsumerProvider(queueName, capturedLogger: logs);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForProcessedLogAsync(logs, @event.EventId);
            var processed = logs.Entries.Single(entry =>
                string.Equals(entry["Result"], IntegrationEventMessageProcessor.ResultProcessed));

            Assert.Equal(OrderSummaryProjectionConsumer.StableHandlerName, (string?)processed["Consumer"]);
            Assert.Equal(OrderCreatedIntegrationEvent.EventType, (string?)processed["EventType"]);
            Assert.Equal(@event.EventId.ToString(), processed["EventId"]?.ToString());
            Assert.Equal(correlationId.ToString(), processed["CorrelationId"]?.ToString());
            Assert.Equal(causationId.ToString(), processed["CausationId"]?.ToString());
            Assert.Equal(1, Convert.ToInt32(processed["Attempt"]));

            await WaitForProjectionAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, OrderSummaryProjectionConsumer.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId]);
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

    private static async Task WaitForConsumerCountAsync(IServiceProvider provider, string queueName, uint expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await ConsumerCountAsync(provider, queueName) >= expected)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"Expected at least {expected} consumers connected to queue '{queueName}' within {Timeout}.");
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

    private static async Task WaitForRetryingLogAsync(
        IntegrationTestCapturingLogger<IntegrationEventMessageProcessor> logs,
        Guid eventId)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var entry = logs.Entries.FirstOrDefault(log =>
                string.Equals(log["Result"], IntegrationEventMessageProcessor.ResultRetrying)
                && string.Equals(log["EventId"]?.ToString(), eventId.ToString()));
            if (entry is not null)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"No structured 'retrying' log observed for event {eventId} within {Timeout}.");
    }

    private static async Task WaitForProcessedLogAsync(
        IntegrationTestCapturingLogger<IntegrationEventMessageProcessor> logs,
        Guid eventId)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var entry = logs.Entries.FirstOrDefault(log =>
                string.Equals(log["Result"], IntegrationEventMessageProcessor.ResultProcessed)
                && string.Equals(log["EventId"]?.ToString(), eventId.ToString()));
            if (entry is not null)
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"No structured 'processed' log observed for event {eventId} within {Timeout}.");
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

    private async Task<OrderSummaryProjectionEntity?> GetProjectionAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OrderSummaryProjections
            .AsNoTracking()
            .FirstOrDefaultAsync(projection => projection.OrderId == orderId);
    }

    private async Task<int> CountProjectionsAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await context.OrderSummaryProjections.CountAsync(projection => projection.OrderId == orderId);
    }

    private async Task<int> CountProcessedAsync(Guid eventId, string handler)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
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

    // RabbitMQ.Client 7.x decodes string header values as byte[]; normalize the same way the consumer
    // processor's ReadHeader does so assertions compare the decoded text.
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
        IEnumerable<Guid> orderIds,
        IEnumerable<Guid> eventIds)
    {
        var orderIdList = orderIds.ToList();
        var orderIdsToDelete = orderIdList.Select(id => new OrderId(id)).ToList();
        var keyList = eventIds.Select(id => id.ToString()).ToList();

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            if (orderIdList.Count > 0)
                await context.OrderSummaryProjections
                    .Where(projection => orderIdList.Contains(projection.OrderId))
                    .ExecuteDeleteAsync();
            if (keyList.Count > 0)
                await context.ProcessedMessages
                    .Where(message => keyList.Contains(message.Key))
                    .ExecuteDeleteAsync();
            if (orderIdsToDelete.Count > 0)
                await context.Orders
                    .Where(order => orderIdsToDelete.Contains(order.Id))
                    .ExecuteDeleteAsync();
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static string UniqueQueueName() => $"it.orders.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static OrderCreatedIntegrationEvent CreateOrderCreated(
        Guid orderId,
        Guid customerId,
        Guid correlationId,
        Guid? causationId) =>
        new(
            orderId,
            customerId,
            $"ORD-IT-{Guid.NewGuid():N}"[..16],
            new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = correlationId,
            CausationId = causationId
        };
}