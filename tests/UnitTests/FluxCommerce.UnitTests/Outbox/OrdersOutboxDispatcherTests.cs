using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Outbox;

/// <summary>
/// Phase 6.7 Orders outbox dispatcher core: claim → deserialize → publish → mark. A failed
/// publication (nack, exception, unknown type, corrupt payload) never loses the message — it is
/// returned to Pending via a retryable-failure transition — and shutdown cancellation propagates
/// without touching the message so stale-claim recovery completes it later.
/// </summary>
public class OrdersOutboxDispatcherTests
{
    private static readonly Guid CorrelationId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task DispatchCoreAsync_RecoversStaleClaims_ThenClaimsOneBatch_WithConfiguredOptions()
    {
        var options = new OutboxDispatchOptions
        {
            BatchSize = 7,
            ClaimTimeout = TimeSpan.FromSeconds(9)
        };
        var store = new Mock<IOutboxStore>();
        var sequence = new MockSequence();
        store.InSequence(sequence)
            .Setup(s => s.RecoverExpiredClaimsAsync(TimeSpan.FromSeconds(9), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        store.InSequence(sequence)
            .Setup(s => s.ClaimPendingAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var publisher = new Mock<IRabbitMQPublisher>();

        await CreateDispatcher(options).DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        store.Verify(s => s.RecoverExpiredClaimsAsync(TimeSpan.FromSeconds(9), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.ClaimPendingAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(p => p.PublishAsync(
            It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchCoreAsync_PublishesDeserializedEvent_WithOriginalMetadata_AndMarksPublished()
    {
        var @event = CreateOrderCreated();
        var message = CreateMessage(@event);
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.Setup(p => p.PublishAsync(
                It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await CreateDispatcher().DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(
            OrderCreatedIntegrationEvent.EventType,
            It.Is<OrderCreatedIntegrationEvent>(e => e == @event),
            It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkPublishedAsync(message.Id, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkRetryableFailureAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchCoreAsync_PublisherReturnsFalse_MarksRetryableFailure_AndDoesNotMarkPublished()
    {
        var message = CreateMessage(CreateOrderCreated());
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.Setup(p => p.PublishAsync(
                It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateDispatcher().DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        store.Verify(s => s.MarkRetryableFailureAsync(
            message.Id, "The message broker did not confirm the publication.", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkPublishedAsync(message.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchCoreAsync_PublisherThrows_MarksRetryableFailure_WithError()
    {
        var message = CreateMessage(CreateOrderCreated());
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.Setup(p => p.PublishAsync(
                It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));

        await CreateDispatcher().DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        store.Verify(s => s.MarkRetryableFailureAsync(
            message.Id, "broker unavailable", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkPublishedAsync(message.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchCoreAsync_UnknownEventType_MarksRetryableFailure_WithoutPublishing()
    {
        var message = CreateMessage(CreateOrderCreated()) with { Type = "orders.unknown" };
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();

        await CreateDispatcher().DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.MarkRetryableFailureAsync(
            message.Id, "Unsupported outbox event type 'orders.unknown'.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchCoreAsync_CorruptPayload_MarksRetryableFailure_WithoutPublishing()
    {
        var message = CreateMessage(CreateOrderCreated()) with
        {
            Payload = "{ not valid json"
        };
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();

        await CreateDispatcher().DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.MarkRetryableFailureAsync(
            message.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkPublishedAsync(message.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchCoreAsync_WhenCancelled_PropagatesAndDoesNotMarkFailure()
    {
        var message = CreateMessage(CreateOrderCreated());
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.Setup(p => p.PublishAsync(
                It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateDispatcher().DispatchCoreAsync(store.Object, publisher.Object, cts.Token));

        store.Verify(s => s.MarkRetryableFailureAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.MarkPublishedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchCoreAsync_FailedMessage_DoesNotBlockTheRestOfTheBatch()
    {
        var first = CreateMessage(CreateOrderCreated());
        var second = CreateMessage(CreateOrderCreated());
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.SetupSequence(p => p.PublishAsync(
                It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        await CreateDispatcher().DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        store.Verify(s => s.MarkRetryableFailureAsync(
            first.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkPublishedAsync(second.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static OrdersOutboxDispatcher CreateDispatcher(OutboxDispatchOptions? options = null) =>
        new(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(options ?? new OutboxDispatchOptions()),
            NullLogger<OrdersOutboxDispatcher>.Instance);

    private static OrderCreatedIntegrationEvent CreateOrderCreated() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-600",
            new DateTimeOffset(2026, 8, 14, 14, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };

    private static OutboxMessage CreateMessage(OrderCreatedIntegrationEvent @event) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Type = OrderCreatedIntegrationEvent.EventType,
            Payload = JsonSerializer.Serialize(@event),
            CorrelationId = @event.CorrelationId,
            CausationId = @event.CausationId,
            OccurredAtUtc = @event.OccurredAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = null,
            Status = OutboxStatus.Pending,
            RetryCount = 0,
            Error = null
        };
}