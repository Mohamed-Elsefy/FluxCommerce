using System.Globalization;
using System.Text;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Xunit;
using DomainReservationStatus = FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 8.7: the Inventory release-request consumer against the real broker and real SQL Server.
/// Verifies the <c>InventoryReleaseRequestedConsumer</c> end to end: delivery → deserialize →
/// Inventory idempotent-processing boundary (release Business State + ProcessedMessage atomic commit) →
/// ACK, staging <c>inventory.released</c> on success, plus the release idempotency semantics: a
/// repeated release (same event) or a later release command against an already-terminal reservation is
/// a benign no-op that never returns reserved quantity twice, while a missing reservation is retried
/// and dead-lettered — never acknowledged without a business effect.
/// </summary>
[Collection(OrderSagaInventoryFlowTestCollection.Name)]
public class InventoryReleaseRequestedConsumerIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly OrderSagaInventoryFlowTestFixture _fixture;

    public InventoryReleaseRequestedConsumerIntegrationTests(OrderSagaInventoryFlowTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Consumer_ReleasesReservation_StagesReleasedOutbox_AndAcknowledges()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var sku = UniqueSku("8.7-RELEASE").ToUpperInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var reservationId = await _fixture.SeedReservationAsync(sku, orderId, 3, expiresAt, onHand: 10);
        var correlationId = Guid.NewGuid();
        await using var provider = _fixture.BuildCompensationProvider(queueName);

        var @event = CreateReleaseRequested(reservationId, correlationId);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);

            await WaitForProcessedAsync(@event.EventId, InventoryReleaseRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            var reservation = await GetReservationAsync(orderId);
            Assert.NotNull(reservation);
            Assert.Equal(DomainReservationStatus.Released, reservation!.Status);

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(0, item.ReservedQuantity);
            Assert.Equal(10, item.AvailableQuantity);

            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryReleaseRequestedConsumer.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var outbox = Assert.Single(await GetOutboxByCorrelationAsync(correlationId));
            Assert.Equal(InventoryReleasedIntegrationEvent.EventType, outbox.Type);
            Assert.Equal(correlationId, outbox.CorrelationId);
            Assert.Equal(@event.EventId, outbox.CausationId);

            var payload = JsonSerializer.Deserialize<InventoryReleasedIntegrationEvent>(outbox.Payload);
            Assert.NotNull(payload);
            Assert.Equal(reservationId, payload!.ReservationId);
            Assert.Equal(orderId, payload.OrderId);
            Assert.Equal(sku, payload.Sku);
            Assert.Equal(3, payload.Quantity);
            Assert.Equal(InventoryReservationStatus.Released, payload.Status);
            Assert.Equal(@event.CorrelationId, payload.CorrelationId);
            Assert.Equal(@event.EventId, payload.CausationId);

            var connectionProvider = provider.GetRequiredService<IRabbitMQConnectionProvider>();
            await using var channel = await connectionProvider.CreateChannelAsync();
            Assert.Null(await channel.BasicGetAsync(queueName, autoAck: true, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, [@event.EventId], [reservationId], sku, [correlationId]);
        }
    }

    [Fact]
    public async Task Consumer_DuplicateDelivery_ReleasesExactlyOnce()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var sku = UniqueSku("8.7-DUP").ToUpperInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var reservationId = await _fixture.SeedReservationAsync(sku, orderId, 2, expiresAt, onHand: 10);
        var correlationId = Guid.NewGuid();
        await using var provider = _fixture.BuildCompensationProvider(queueName);

        var @event = CreateReleaseRequested(reservationId, correlationId);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, @event);
            await PublishAsync(provider, @event);

            await WaitForProcessedAsync(@event.EventId, InventoryReleaseRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProcessedAsync(@event.EventId, InventoryReleaseRequestedConsumer.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var reservation = await GetReservationAsync(orderId);
            Assert.NotNull(reservation);
            Assert.Equal(DomainReservationStatus.Released, reservation!.Status);

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(0, item.ReservedQuantity);
            Assert.Equal(10, item.AvailableQuantity);

            // The duplicate delivery is skipped by the idempotent-processing boundary: exactly one
            // released outbox record is staged.
            var released = await GetOutboxByCorrelationAsync(correlationId);
            Assert.Single(released);
        }
        finally
        {
            await CleanupAsync(provider, queueName, orderId, [@event.EventId], [reservationId], sku, [correlationId]);
        }
    }

    [Fact]
    public async Task Consumer_ReleaseCommandForAlreadyReleasedReservation_IsBenignNoOp()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var sku = UniqueSku("8.7-ALREADY").ToUpperInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var reservationId = await _fixture.SeedReservationAsync(sku, orderId, 2, expiresAt, onHand: 10);
        var firstCorrelationId = Guid.NewGuid();
        var secondCorrelationId = Guid.NewGuid();
        await using var provider = _fixture.BuildCompensationProvider(queueName);

        var firstRelease = CreateReleaseRequested(reservationId, firstCorrelationId);
        var laterRelease = CreateReleaseRequested(reservationId, secondCorrelationId);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            await PublishAsync(provider, firstRelease);
            await WaitForProcessedAsync(firstRelease.EventId, InventoryReleaseRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);

            // A later, distinct release command for the same (now terminal) reservation is a benign
            // no-op: acknowledged, no released event, nothing dead-lettered.
            await PublishAsync(provider, laterRelease);
            await WaitForProcessedAsync(laterRelease.EventId, InventoryReleaseRequestedConsumer.StableHandlerName);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(1, await CountProcessedAsync(firstRelease.EventId, InventoryReleaseRequestedConsumer.StableHandlerName));
            Assert.Equal(1, await CountProcessedAsync(laterRelease.EventId, InventoryReleaseRequestedConsumer.StableHandlerName));
            Assert.Equal(0u, await GetMessageCountAsync(provider, DeadLetterQueueName(queueName)));

            var reservation = await GetReservationAsync(orderId);
            Assert.NotNull(reservation);
            Assert.Equal(DomainReservationStatus.Released, reservation!.Status);

            var item = await GetItemAsync(sku);
            Assert.NotNull(item);
            Assert.Equal(0, item.ReservedQuantity);
            Assert.Equal(10, item.AvailableQuantity);

            // Only the first release staged inventory.released.
            Assert.Single(await GetOutboxByCorrelationAsync(firstCorrelationId));
            Assert.Empty(await GetOutboxByCorrelationAsync(secondCorrelationId));
        }
        finally
        {
            await CleanupAsync(
                provider,
                queueName,
                orderId,
                [firstRelease.EventId, laterRelease.EventId],
                [reservationId],
                sku,
                [firstCorrelationId, secondCorrelationId]);
        }
    }

    [Fact]
    public async Task Consumer_MissingReservation_IsRetriedAndDeadLettered()
    {
        var queueName = UniqueQueueName();
        var orderId = Guid.NewGuid();
        var sku = UniqueSku("8.7-MISSING").ToUpperInvariant();
        await _fixture.SeedItemAsync(sku, 10);
        var missingReservationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        await using var provider = _fixture.BuildCompensationProvider(queueName);

        var @event = CreateReleaseRequested(missingReservationId, correlationId);

        try
        {
            var consumer = await StartConsumerAsync(provider);
            await WaitForConsumerAsync(provider, queueName);

            // The reservation does not exist: the release cannot be acknowledged without a business
            // effect, so the consumer throws, the infrastructure retries, and the delivery is
            // dead-lettered — the release is never silently lost.
            await PublishAsync(provider, @event);

            await WaitForMessageCountAsync(provider, DeadLetterQueueName(queueName), 1);
            await WaitForQueueEmptyAsync(provider, queueName);
            await consumer.StopAsync(CancellationToken.None);

            Assert.Equal(0, await CountProcessedAsync(@event.EventId, InventoryReleaseRequestedConsumer.StableHandlerName));
            Assert.Equal(0, await CountReservationsAsync(orderId));
            Assert.Empty(await GetOutboxByCorrelationAsync(correlationId));

            var (body, headers) = await GetSingleDeadLetteredMessageAsync(provider, queueName);
            Assert.NotNull(body);
            var bodyText = Encoding.UTF8.GetString(body);
            Assert.True(
                bodyText.Contains(missingReservationId.ToString()),
                $"DLQ body ({bodyText.Length} chars) did not contain '{missingReservationId}'. Body: {bodyText}");
            Assert.Equal(
                IntegrationEventMessageProcessor.ExhaustedReason,
                HeaderString(headers[IntegrationEventMessageProcessor.DeadLetterReasonHeader]));
        }
        finally
        {
            await CleanupAsync(
                provider,
                queueName,
                orderId,
                [@event.EventId],
                [missingReservationId],
                sku,
                [correlationId]);
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

    private static async Task PublishAsync(IServiceProvider provider, InventoryReleaseRequestedIntegrationEvent @event)
    {
        var publisher = provider.GetRequiredService<IRabbitMQPublisher>();
        var confirmed = await publisher.PublishAsync(InventoryReleaseRequestedIntegrationEvent.EventType, @event);
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

    private async Task<IReadOnlyList<OutboxMessage>> GetOutboxByCorrelationAsync(Guid correlationId)
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
        Guid orderId,
        IReadOnlyCollection<Guid> eventIds,
        IReadOnlyCollection<Guid> reservationIds,
        string sku,
        IEnumerable<Guid>? correlations = null)
    {
        var orderIdValue = new OrderId(orderId);
        var keyList = eventIds.Select(id => id.ToString()).ToList();
        var correlationList = (correlations ?? []).ToList();

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await context.InventoryReservations
                .Where(reservation => reservation.OrderId == orderIdValue)
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

    private static string UniqueQueueName() => $"it.inventory.q.{Guid.NewGuid():N}";

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string DeadLetterExchangeName(string queueName) => $"{queueName}.dlx";

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];

    private static InventoryReleaseRequestedIntegrationEvent CreateReleaseRequested(
        Guid reservationId,
        Guid correlationId) =>
        new(reservationId)
        {
            CorrelationId = correlationId
        };
}