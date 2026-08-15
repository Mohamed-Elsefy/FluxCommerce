using System.Text.Json;
using System.Text.Json.Nodes;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Messaging.Versioning;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace FluxCommerce.UnitTests.Messaging;

/// <summary>
/// Phase 6.6: the RabbitMQ publisher's serialization and metadata contract. Verifies the published
/// body is serialized from the runtime concrete type (matching the Outbox payload contract) and that
/// the canonical event metadata is propagated both as AMQP properties and transport headers. The
/// broker-facing flow (confirms, delivery, routing) is covered by the RabbitMQ integration tests.
/// </summary>
public class RabbitMQPublisherTests
{
    private static readonly Guid CorrelationId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void SerializeBody_UsesRuntimeType_PreservingPayloadAndMetadata()
    {
        var original = CreateOrderCreated();

        var body = RabbitMQPublisher.SerializeBody(original);

        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(original), body);

        var roundTripped = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(body);
        Assert.NotNull(roundTripped);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void SerializeBody_WritesBusinessPayload_NotJustInterfaceMetadata()
    {
        var json = JsonNode.Parse(RabbitMQPublisher.SerializeBody(CreateOrderCreated()))!.AsObject();

        Assert.Contains("OrderId", json.Select(property => property.Key));
        Assert.Contains("OrderNumber", json.Select(property => property.Key));
        Assert.Equal("1.0", (string)json["Version"]!);
        Assert.Equal(CorrelationId.ToString(), (string)json["CorrelationId"]!);
    }

    [Fact]
    public void BuildProperties_SetsAmqpProperties_FromEventMetadata()
    {
        var @event = CreateOrderCreated();

        var properties = RabbitMQPublisher.BuildProperties(OrderCreatedIntegrationEvent.EventType, @event);

        Assert.Equal(@event.EventId.ToString(), properties.MessageId);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, properties.Type);
        Assert.Equal(@event.CorrelationId.ToString(), properties.CorrelationId);
        Assert.True(properties.Persistent);
        Assert.Equal(DeliveryModes.Persistent, properties.DeliveryMode);
        Assert.Equal(
            new AmqpTimestamp(new DateTimeOffset(@event.OccurredAtUtc).ToUnixTimeSeconds()),
            properties.Timestamp);
    }

    [Fact]
    public void BuildProperties_SetsHeaders_ForTracingAndVersioning()
    {
        var causationId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var @event = CreateOrderCreated() with { CausationId = causationId };

        var headers = RabbitMQPublisher.BuildProperties(OrderCreatedIntegrationEvent.EventType, @event).Headers;
        Assert.NotNull(headers);

        Assert.Equal(@event.EventId.ToString(), headers!["EventId"]);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, headers["EventType"]);
        Assert.Equal(EventVersion.V1.ToString(), headers["EventVersion"]);
        Assert.Equal(CorrelationId.ToString(), headers["CorrelationId"]);
        Assert.Equal(causationId.ToString(), headers["CausationId"]);
        Assert.Equal(@event.OccurredAtUtc.ToString("O"), headers["OccurredAtUtc"]);
    }

    [Fact]
    public void BuildProperties_WhenCausationIdAbsent_SendsEmptyHeader()
    {
        var headers = RabbitMQPublisher.BuildProperties(
            OrderCreatedIntegrationEvent.EventType,
            CreateOrderCreated()).Headers;
        Assert.NotNull(headers);

        Assert.Equal(string.Empty, headers!["CausationId"]);
    }

    [Fact]
    public async Task PublishAsync_NullEvent_ThrowsArgumentNullException()
    {
        var publisher = new RabbitMQPublisher(
            Mock.Of<IRabbitMQConnectionProvider>(),
            NullLogger<RabbitMQPublisher>.Instance,
            "fluxcommerce.events");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, null!));
    }

    [Fact]
    public async Task PublishAsync_BlankEventType_ThrowsArgumentException()
    {
        var publisher = new RabbitMQPublisher(
            Mock.Of<IRabbitMQConnectionProvider>(),
            NullLogger<RabbitMQPublisher>.Instance,
            "fluxcommerce.events");

        await Assert.ThrowsAsync<ArgumentException>(
            () => publisher.PublishAsync(" ", CreateOrderCreated()));
    }

    [Fact]
    public async Task PublishAsync_ReusesTheSharedChannel_AcrossPublishes()
    {
        var channel = new Mock<IChannel>();
        channel.SetupGet(c => c.IsOpen).Returns(true);

        var provider = new Mock<IRabbitMQConnectionProvider>();
        provider
            .Setup(p => p.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(channel.Object));

        var publisher = new RabbitMQPublisher(
            provider.Object,
            NullLogger<RabbitMQPublisher>.Instance,
            "fluxcommerce.events");

        try
        {
            await publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, CreateOrderCreated());
            await publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, CreateOrderCreated());

            provider.Verify(
                p => p.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            await publisher.DisposeAsync();
        }
    }

    [Fact]
    public async Task PublishAsync_WhenChannelClosed_CreatesANewChannel()
    {
        var firstChannel = new Mock<IChannel>();
        var isOpen = true;
        firstChannel.SetupGet(c => c.IsOpen).Returns(() => isOpen);

        var secondChannel = new Mock<IChannel>();
        secondChannel.SetupGet(c => c.IsOpen).Returns(true);

        var provider = new Mock<IRabbitMQConnectionProvider>();
        provider
            .SetupSequence(p => p.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(firstChannel.Object))
            .Returns(Task.FromResult(secondChannel.Object));

        var publisher = new RabbitMQPublisher(
            provider.Object,
            NullLogger<RabbitMQPublisher>.Instance,
            "fluxcommerce.events");

        try
        {
            await publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, CreateOrderCreated());
            isOpen = false;
            await publisher.PublishAsync(OrderCreatedIntegrationEvent.EventType, CreateOrderCreated());

            provider.Verify(
                p => p.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
        finally
        {
            await publisher.DisposeAsync();
        }
    }

    private static OrderCreatedIntegrationEvent CreateOrderCreated() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-600",
            new DateTimeOffset(2026, 8, 14, 14, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };
}