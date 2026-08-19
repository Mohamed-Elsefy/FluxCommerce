using System.Globalization;
using System.Text;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 8.4: the Inventory reservation-request consumer against the real broker and real SQL Server.
/// Verifies the <c>InventoryReservationRequestedConsumer</c> end to end: delivery → deserialize →
/// Inventory idempotent-processing boundary (reservation Business State + ProcessedMessage atomic
/// commit) → ACK, plus idempotent duplicate delivery and the failure semantics (a reservation that
/// cannot be created is retried and dead-lettered — never acknowledged without a business effect).
/// </summary>
[Collection(RabbitMQInventoryConsumerTestCollection.Name)]
public class InventoryReservationRequestedConsumerIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly RabbitMQInventoryConsumerTestFixture _fixture;

    public InventoryReservationRequestedConsumerIntegrationTests(RabbitMQInventoryConsumerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Consumer_ReservesInventory_AndAcknowledges()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var sku = UniqueSku("8.4-RESERVE").ToUpperInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        await _fixture.SeedItemAsync(sku, 10);
        var @event = CreateReservationRequested(orderId, sku, 3, expiresAt);
        await using var provider = _fixture.BuildConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForReservationAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, InventoryReservationRequestedConsumer.StableHandlerName);
            await AckSettleAsync();
            await consumer.StopAsync(CancellationToken.None);

            var reservation = await GetReservationAsync(orderId);
            Assert.NotNull(reservation);
            Assert.Equal(sku, reservation!.Sku.Value);
            Assert.Equal(3, reservation.Quantity);
            Assert.Equal(InventoryReservationStatus.Pending, reservation.Status);
            Assert.Equal(expiresAt, reservation.ExpiresAt);

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(3, item.ReservedQuantity);
            Assert.Equal(7, item.AvailableQuantity);

            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryReservationRequestedConsumer.StableHandlerName));

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId], [sku]);
        }
    }

    [Fact]
    public async Task Consumer_DuplicateDelivery_ReservesExactlyOnce()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var sku = UniqueSku("8.4-DUP").ToUpperInvariant();
        await _fixture.SeedItemAsync(sku, 10);
        var @event = CreateReservationRequested(orderId, sku, 2, DateTimeOffset.UtcNow.AddMinutes(30));
        await using var provider = _fixture.BuildConsumerProvider(queueName);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);
            await PublishAsync(provider, @event);

            await WaitForReservationAsync(orderId);
            await WaitForProcessedAsync(@event.EventId, InventoryReservationRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountReservationsAsync(orderId));
            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryReservationRequestedConsumer.StableHandlerName));

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(2, item.ReservedQuantity);
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId], [sku]);
        }
    }

    [Fact]
    public async Task Consumer_InsufficientStock_RetriesThenDeadLetters_WithoutBusinessEffect()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var sku = UniqueSku("8.4-NOSTOCK").ToUpperInvariant();
        await _fixture.SeedItemAsync(sku, 1);
        var @event = CreateReservationRequested(orderId, sku, 2, DateTimeOffset.UtcNow.AddMinutes(30));
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

            // The reservation cannot be created (stock 1, requested 2): every attempt fails with a
            // retryable business failure, then the policy is exhausted and the delivery dead-letters —
            // with no reservation and no ProcessedMessage record left behind (never a silent ACK).
            await PublishAsync(provider, @event);

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(0, await CountReservationsAsync(orderId));
            Assert.Equal(0, await CountProcessedAsync(@event.EventId, InventoryReservationRequestedConsumer.StableHandlerName));

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(0, item.ReservedQuantity);
            Assert.Equal(1, item.AvailableQuantity);

            var deadLettered = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.Equal(
                IntegrationEventMessageProcessor.ExhaustedReason,
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterReasonHeader]));
            Assert.Equal(
                2,
                Convert.ToInt32(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterAttemptHeader]));
            Assert.Equal(
                InventoryReservationRequestedConsumer.StableHandlerName,
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterConsumerHeader]));
            Assert.Equal(
                nameof(InvalidOperationException),
                HeaderString(deadLettered.Headers[IntegrationEventMessageProcessor.DeadLetterErrorTypeHeader]));
        }
        finally
        {
            await CleanupAsync(provider, queueName, [orderId], [@event.EventId], [sku]);
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

    private static async Task PublishAsync(IServiceProvider provider, InventoryReservationRequestedIntegrationEvent @event)
    {
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var confirmed = await publisher.PublishAsync(InventoryReservationRequestedIntegrationEvent.EventType, @event);
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

    private static Task AckSettleAsync() => Task.Delay(300);

    private async Task<InventoryReservation?> GetReservationAsync(Guid orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await context.InventoryReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(reservation => reservation.OrderId == new OrderId(orderId));
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
        return await context.InventoryReservations.CountAsync(reservation => reservation.OrderId == new OrderId(orderId));
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
        IEnumerable<Guid> eventIds,
        IEnumerable<string> skus)
    {
        var orderIdList = orderIds.Select(id => new OrderId(id)).ToList();
        var keyList = eventIds.Select(id => id.ToString()).ToList();
        var itemSkus = skus.Select(sku => new InventorySku(sku)).ToList();

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            if (orderIdList.Count > 0)
                await context.InventoryReservations
                    .Where(reservation => orderIdList.Contains(reservation.OrderId))
                    .ExecuteDeleteAsync();
            if (keyList.Count > 0)
                await context.ProcessedMessages
                    .Where(message => keyList.Contains(message.Key))
                    .ExecuteDeleteAsync();
            if (itemSkus.Count > 0)
                await context.InventoryItems
                    .Where(item => itemSkus.Contains(item.Id))
                    .ExecuteDeleteAsync();
        }

        await DeleteQueueAsync(provider, queueName);
    }

    private static string UniqueQueueName() => $"it.inventory.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];

    private static InventoryReservationRequestedIntegrationEvent CreateReservationRequested(
        Guid orderId,
        string sku,
        int quantity,
        DateTimeOffset expiresAt) =>
        new(orderId, sku, quantity, expiresAt)
        {
            CorrelationId = Guid.NewGuid()
        };
}