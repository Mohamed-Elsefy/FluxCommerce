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