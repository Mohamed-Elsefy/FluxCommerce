using System.Text;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 6.6: RabbitMQPublisher against the real broker. Verifies that a confirmed publish is
/// delivered to a bound queue with the correct routing key (the event type), exchange, AMQP
/// properties and transport headers, that an unrouted publish is still confirmed (broker acceptance,
/// not routing, is what confirms verify), and that an unreachable broker never reports success.
/// </summary>
[Collection(RabbitMQTestCollection.Name)]
public class RabbitMQPublisherIntegrationTests
{
    private static readonly Guid CorrelationId = new("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly RabbitMQTestFixture _fixture;

    public RabbitMQPublisherIntegrationTests(RabbitMQTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublishAsync_ConfirmedMessage_IsDeliveredToBoundQueue_WithMetadata()
    {
        var @event = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-IT-1",
            new DateTimeOffset(2026, 8, 14, 15, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };

        var queueName = $"it.q.{Guid.NewGuid():N}";

        await using var channel = await _fixture.Provider.CreateChannelAsync();
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null!,
            passive: false,
            noWait: false,
            cancellationToken: default);
        await channel.QueueBindAsync(
            queue: queueName,
            exchange: RabbitMQTestFixture.TestExchangeName,
            routingKey: OrderCreatedIntegrationEvent.EventType,
            arguments: null!,
            noWait: false,
            cancellationToken: default);

        try
        {
            var confirmed = await _fixture.Publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, @event);

            Assert.True(confirmed);

            var result = await channel.BasicGetAsync(queue: queueName, autoAck: true, cancellationToken: default);

            Assert.NotNull(result);
            Assert.Equal(OrderCreatedIntegrationEvent.EventType, result!.RoutingKey);
            Assert.Equal(RabbitMQTestFixture.TestExchangeName, result.Exchange);

            var properties = result.BasicProperties;
            Assert.Equal(@event.EventId.ToString(), properties.MessageId);
            Assert.Equal(OrderCreatedIntegrationEvent.EventType, properties.Type);
            Assert.Equal(@event.CorrelationId.ToString(), properties.CorrelationId);
            Assert.True(properties.Persistent);
            Assert.Equal(
                new DateTimeOffset(@event.OccurredAtUtc).ToUnixTimeSeconds(),
                properties.Timestamp.UnixTime);

            Assert.Equal(@event.EventId.ToString(), Header(properties, "EventId"));
            Assert.Equal(OrderCreatedIntegrationEvent.EventType, Header(properties, "EventType"));
            Assert.Equal(EventVersion.V1.ToString(), Header(properties, "EventVersion"));
            Assert.Equal(@event.CorrelationId.ToString(), Header(properties, "CorrelationId"));
            Assert.Equal(string.Empty, Header(properties, "CausationId"));
            Assert.Equal(@event.OccurredAtUtc.ToString("O"), Header(properties, "OccurredAtUtc"));

            var delivered = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(result.Body.Span);
            Assert.NotNull(delivered);
            Assert.Equal(@event, delivered);
        }
        finally
        {
            await channel.QueueDeleteAsync(
                queue: queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken: default);
        }
    }

    [Fact]
    public async Task PublishAsync_ReturnsTrue_WhenBrokerConfirms_WithoutBoundQueue()
    {
        var @event = new InventoryReservationExpiredIntegrationEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SKU-IT",
            2,
            new DateTimeOffset(2026, 8, 14, 16, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };

        // mandatory:false with no bound queue is still confirmed by the broker: publisher confirms
        // verify broker acceptance, so success must not depend on a consumer being routed to.
        var confirmed = await _fixture.Publisher.PublishAsync(
            InventoryReservationExpiredIntegrationEvent.EventType,
            @event);

        Assert.True(confirmed);
    }

    [Fact]
    public async Task PublishAsync_WhenBrokerUnreachable_DoesNotReportSuccess()
    {
        const string unreachableConnection = "amqp://fluxcommerce:FluxCommerce_SR_MB_3030%21%21@localhost:5999/";

        var publisher = new RabbitMQPublisher(
            new RabbitMQConnectionProvider(unreachableConnection),
            NullLogger<RabbitMQPublisher>.Instance,
            RabbitMQTestFixture.TestExchangeName);

        var @event = new OrderCreatedIntegrationEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-IT-2",
            new DateTimeOffset(2026, 8, 14, 17, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };

        await Assert.ThrowsAsync<BrokerUnreachableException>(
            () => publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, @event));
    }

    private static string Header(IReadOnlyBasicProperties properties, string name)
    {
        var value = properties.Headers![name];
        return value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString() ?? string.Empty;
    }
}