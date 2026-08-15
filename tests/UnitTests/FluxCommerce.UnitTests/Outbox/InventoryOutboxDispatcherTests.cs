using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Outbox;

/// <summary>
/// Phase 6.7 Inventory outbox dispatcher core. Mirrors the Orders dispatcher; the Inventory-specific
/// behavior is the event-type → contract mapping for <c>inventory.reservation_expired</c>.
/// </summary>
public class InventoryOutboxDispatcherTests
{
    private static readonly Guid CorrelationId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task DispatchCoreAsync_PublishesDeserializedEvent_WithOriginalMetadata_AndMarksPublished()
    {
        var @event = CreateReservationExpired();
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
            InventoryReservationExpiredIntegrationEvent.EventType,
            It.Is<InventoryReservationExpiredIntegrationEvent>(e => e == @event),
            It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.MarkPublishedAsync(message.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchCoreAsync_PublisherReturnsFalse_MarksRetryableFailure_AndDoesNotMarkPublished()
    {
        var message = CreateMessage(CreateReservationExpired());
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
    public async Task DispatchCoreAsync_UnknownEventType_MarksRetryableFailure_WithoutPublishing()
    {
        var message = CreateMessage(CreateReservationExpired()) with { Type = "inventory.unknown" };
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();

        await CreateDispatcher().DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.MarkRetryableFailureAsync(
            message.Id, "Unsupported outbox event type 'inventory.unknown'.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchCoreAsync_WhenCancelled_PropagatesAndDoesNotMarkFailure()
    {
        var message = CreateMessage(CreateReservationExpired());
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
    }

    private static InventoryOutboxDispatcher CreateDispatcher(OutboxDispatchOptions? options = null) =>
        new(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(options ?? new OutboxDispatchOptions()),
            NullLogger<InventoryOutboxDispatcher>.Instance);

    private static InventoryReservationExpiredIntegrationEvent CreateReservationExpired() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SKU-1",
            2,
            new DateTimeOffset(2026, 8, 14, 15, 0, 0, TimeSpan.Zero))
        {
            CorrelationId = CorrelationId
        };

    private static OutboxMessage CreateMessage(InventoryReservationExpiredIntegrationEvent @event) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Type = InventoryReservationExpiredIntegrationEvent.EventType,
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