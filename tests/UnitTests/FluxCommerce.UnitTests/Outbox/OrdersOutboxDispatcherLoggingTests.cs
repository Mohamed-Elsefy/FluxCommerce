using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Outbox;

/// <summary>
/// Phase 6.9: the Orders outbox dispatcher's structured logging contract. Every dispatch outcome log
/// carries the identifiers an operator needs to correlate an attempt back to a business operation
/// (MessageId, EventId, EventType, CorrelationId) plus the attempt/outcome/error detail, and never
/// writes the serialized business payload.
/// </summary>
public class OrdersOutboxDispatcherLoggingTests
{
    private static readonly Guid CorrelationId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task DispatchCoreAsync_Success_LogsStructuredIdentifiers_AndNeverLogsPayload()
    {
        var @event = CreateOrderCreated();
        var message = CreateMessage(@event) with { RetryCount = 1 };
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.Setup(p => p.PublishAsync(
                It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var logger = new CapturingLogger<OrdersOutboxDispatcher>();

        await CreateDispatcher(logger).DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        var entry = logger.Single(LogLevel.Information);
        Assert.NotNull(entry);
        Assert.Equal(message.Id, entry!["MessageId"]);
        Assert.Equal(message.EventId, entry["EventId"]);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, entry["EventType"]);
        Assert.Equal(CorrelationId, entry["CorrelationId"]);
        Assert.Equal(2, entry["Attempt"]);

        Assert.DoesNotContain(logger.Entries, logged => logged.Message.Contains("ORD-LOG-1"));
    }

    [Fact]
    public async Task DispatchCoreAsync_RetryableFailure_LogsStructuredIdentifiers_WithErrorAndRetryAfter()
    {
        var message = CreateMessage(CreateOrderCreated()) with { RetryCount = 0 };
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.Setup(p => p.PublishAsync(
                It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var logger = new CapturingLogger<OrdersOutboxDispatcher>();

        var started = DateTime.UtcNow;
        await CreateDispatcher(logger, new OutboxDispatchOptions { BaseRetryDelay = TimeSpan.FromSeconds(2) })
            .DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        var entry = logger.Single(LogLevel.Warning);
        Assert.NotNull(entry);
        Assert.Equal(message.Id, entry!["MessageId"]);
        Assert.Equal(message.EventId, entry["EventId"]);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, entry["EventType"]);
        Assert.Equal(CorrelationId, entry["CorrelationId"]);
        Assert.Equal(1, entry["Attempt"]);
        Assert.Equal("The message broker did not confirm the publication.", entry["Error"]);

        // First failure (RetryCount 0 → attempt 1) is scheduled one BaseRetryDelay (2s) from the dispatch.
        var retryAfterUtc = Assert.IsType<DateTime>(entry["RetryAfterUtc"]);
        Assert.True(
            retryAfterUtc >= started.AddSeconds(1.5) && retryAfterUtc <= started.AddSeconds(3),
            $"retryAfterUtc={retryAfterUtc:O} started={started:O} expected={started.AddSeconds(2):O}");
    }

    [Fact]
    public async Task DispatchCoreAsync_TerminalFailure_LogsStructuredIdentifiers_WithError()
    {
        var message = CreateMessage(CreateOrderCreated()) with { RetryCount = 5 };
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.Setup(p => p.PublishAsync(
                It.IsAny<string>(), It.IsAny<IIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var logger = new CapturingLogger<OrdersOutboxDispatcher>();

        await CreateDispatcher(logger, new OutboxDispatchOptions { MaxRetryCount = 5 })
            .DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        var entry = logger.Single(LogLevel.Error);
        Assert.NotNull(entry);
        Assert.Equal(message.Id, entry!["MessageId"]);
        Assert.Equal(message.EventId, entry["EventId"]);
        Assert.Equal(OrderCreatedIntegrationEvent.EventType, entry["EventType"]);
        Assert.Equal(CorrelationId, entry["CorrelationId"]);
        Assert.Equal(5, entry["MaxRetryCount"]);
        Assert.Equal("The message broker did not confirm the publication.", entry["Error"]);
    }

    [Fact]
    public async Task DispatchCoreAsync_NonRetryableFailure_LogsStructuredIdentifiers_WithError()
    {
        var message = CreateMessage(CreateOrderCreated()) with { Type = "orders.unknown" };
        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var publisher = new Mock<IRabbitMQPublisher>();
        var logger = new CapturingLogger<OrdersOutboxDispatcher>();

        await CreateDispatcher(logger).DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        var entry = logger.Single(LogLevel.Error);
        Assert.NotNull(entry);
        Assert.Equal(message.Id, entry!["MessageId"]);
        Assert.Equal(message.EventId, entry["EventId"]);
        Assert.Equal("orders.unknown", entry["EventType"]);
        Assert.Equal(CorrelationId, entry["CorrelationId"]);
        Assert.Equal("Unsupported outbox event type 'orders.unknown'.", entry["Error"]);
    }

    private static OrdersOutboxDispatcher CreateDispatcher(
        CapturingLogger<OrdersOutboxDispatcher> logger,
        OutboxDispatchOptions? options = null) =>
        new(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(options ?? new OutboxDispatchOptions()),
            logger);

    private static OrderCreatedIntegrationEvent CreateOrderCreated() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-LOG-1",
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
