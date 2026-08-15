using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Outbox;

/// <summary>
/// Phase 6.9: Inventory outbox dispatcher structured logging parity — the success log carries the same
/// correlation identifiers as the Orders dispatcher.
/// </summary>
public class InventoryOutboxDispatcherLoggingTests
{
    private static readonly Guid CorrelationId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task DispatchCoreAsync_Success_LogsStructuredIdentifiers_AndNeverLogsPayload()
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
        var logger = new CapturingLogger<InventoryOutboxDispatcher>();

        await CreateDispatcher(logger).DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        var entry = logger.Single(LogLevel.Information);
        Assert.NotNull(entry);
        Assert.Equal(message.Id, entry!["MessageId"]);
        Assert.Equal(message.EventId, entry["EventId"]);
        Assert.Equal(InventoryReservationExpiredIntegrationEvent.EventType, entry["EventType"]);
        Assert.Equal(CorrelationId, entry["CorrelationId"]);
        Assert.Equal(1, entry["Attempt"]);

        Assert.DoesNotContain(logger.Entries, logged => logged.Message.Contains("SKU-LOG-1"));
    }

    private static InventoryOutboxDispatcher CreateDispatcher(
        CapturingLogger<InventoryOutboxDispatcher> logger,
        OutboxDispatchOptions? options = null) =>
        new(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(options ?? new OutboxDispatchOptions()),
            logger);

    private static InventoryReservationExpiredIntegrationEvent CreateReservationExpired() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SKU-LOG-1",
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
