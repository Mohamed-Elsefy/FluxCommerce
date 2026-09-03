using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.Common.FaultInjection;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests.Reliability;

/// <summary>
/// Phase 12.3 — Outbox &amp; Dispatcher Failure Testing. Validates broker outages, stale-claim
/// recovery, retry publication and duplicate tolerance without losing messages. Uses SQLite
/// via ReliabilityTestBase with SQLite-compatible dispatch simulation (production SQL Server
/// claim SQL is intentionally bypassed; invariants are verified via DbContext semantics).
/// </summary>
[Trait("Category", "Reliability")]
public sealed class OutboxDispatcherFailureTests : ReliabilityTestBase
{
    private readonly IntegrationEventOutboxMessageFactory _factory = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        // In-memory publisher that records and confirms. BrokerFaultSimulator (wrapping via
        // ReliabilityTestBase.TryWrapPublisherWithBrokerSimulator) will inject failures on top.
        services.AddSingleton<IRabbitMQPublisher>(sp => new RecordingPublisher());
    }

    private BrokerFaultSimulator Broker => BrokerFaultSimulatorInstance!;

    // ------------------------------------------------ Broker unavailability — business commits, outbox pending

    [Fact]
    public async Task Order_BusinessCommits_WhileBrokerUnavailable_OutboxRemainsPending_NoLoss()
    {
        // Business action persists Order + Outbox atomically; broker outage must not roll back business.
        var order = CreateOrder();
        var domainEvent = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var integration = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, domainEvent.OccurredAtUtc)
        {
            EventId = domainEvent.EventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = domainEvent.OccurredAtUtc
        };
        var outbox = _factory.Create(OrderCreatedIntegrationEvent.EventType, integration);

        OrdersContext.Orders.Add(order);
        OrdersContext.OutboxMessages.Add(outbox);
        await OrdersContext.SaveChangesAsync();

        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
        Assert.Equal(1, await OrdersContext.OutboxMessages.CountAsync());
        var pending = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, pending.Status);
        Assert.Equal(0, pending.RetryCount);

        // Arm broker outage and attempt dispatch — must not lose message.
        Broker.SimulateConnectionOutage(times: 1);
        var dispatched = await SimulateDispatchOneAsync(OrdersContext, pending);

        Assert.False(dispatched); // publish failed
        var afterFail = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, afterFail.Status); // retryable, still pending
        Assert.Equal(1, afterFail.RetryCount);
        Assert.NotNull(afterFail.RetryAfterUtc);
        Assert.NotNull(afterFail.Error);
        Assert.Contains("connection outage", afterFail.Error, StringComparison.OrdinalIgnoreCase);

        // Business still committed.
        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
        Assert.Equal(1, await OrdersContext.OutboxMessages.CountAsync()); // zero loss

        ResetFaults();
    }

    [Fact]
    public async Task Inventory_BrokerPublishRejection_OutboxStaysPending_BusinessDurable()
    {
        var sku = new InventorySku($"INV-BR-{Guid.NewGuid():N}"[..8].ToUpperInvariant());
        var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item);

        var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent(
            Guid.NewGuid(), sku.Value, 3, DateTimeOffset.UtcNow.AddMinutes(15))
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        var outbox = _factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent.EventType, integration);
        InventoryContext.OutboxMessages.Add(outbox);
        await InventoryContext.SaveChangesAsync();

        Broker.SimulatePublishRejection(times: 1);
        var pending = await InventoryContext.OutboxMessages.AsNoTracking().SingleAsync();
        var ok = await SimulateDispatchOneAsync(InventoryContext, pending);
        Assert.False(ok);

        var after = await InventoryContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, after.Status);
        Assert.Equal(1, after.RetryCount);
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync());

        ResetFaults();
    }

    [Fact]
    public async Task Payment_BrokerPublishException_OutboxRetryable_BusinessNotLost()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 42m, "USD", Guid.NewGuid());
        PaymentsContext.Payments.Add(payment);

        var integration = new FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent(
            payment.OrderId, payment.PaymentId, "pi_test_brk", 42m, "USD", "Succeeded", DateTimeOffset.UtcNow)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = payment.OrderId,
            OccurredAtUtc = DateTime.UtcNow
        };
        var outbox = _factory.Create(FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent.EventType, integration);
        PaymentsContext.OutboxMessages.Add(outbox);
        await PaymentsContext.SaveChangesAsync();

        Broker.SimulatePublishException(times: 1);
        var pending = await PaymentsContext.OutboxMessages.AsNoTracking().SingleAsync();
        var ok = await SimulateDispatchOneAsync(PaymentsContext, pending);
        Assert.False(ok);
        var after = await PaymentsContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, after.Status);
        Assert.Equal(1, await PaymentsContext.Payments.CountAsync());
        Assert.Equal(1, await PaymentsContext.OutboxMessages.CountAsync());

        ResetFaults();
    }

    // ------------------------------------------------ Stale claim recovery

    [Fact]
    public async Task StaleClaim_Recovery_Releases_InProgress_ToPending_AfterTimeout()
    {
        // Simulate dispatcher crash: claimed batch but never published (left InProgress).
        var order = CreateOrder();
        var domainEvent = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var integration = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, domainEvent.OccurredAtUtc)
        {
            EventId = domainEvent.EventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = domainEvent.OccurredAtUtc
        };
        var outbox = _factory.Create(OrderCreatedIntegrationEvent.EventType, integration);

        OrdersContext.Orders.Add(order);
        OrdersContext.OutboxMessages.Add(outbox);
        await OrdersContext.SaveChangesAsync();

        // Claim: move to InProgress with old timestamp to simulate crash (stale).
        var msg = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        await OrdersContext.OutboxMessages.Where(m => m.Id == msg.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.InProgress).SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow.AddMinutes(-10)));

        Assert.Equal(OutboxStatus.InProgress, (await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync()).Status);

        // Recovery: dispatcher restart recovers expired claims.
        var recovered = await RecoverExpiredClaimsSQLiteAsync(OrdersContext, TimeSpan.FromSeconds(30));
        Assert.Equal(1, recovered);

        var after = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, after.Status);
        Assert.Null(after.ProcessedAtUtc); // cleared for re-claim
        // Business still there, no loss.
        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
    }

    [Fact]
    public async Task StaleClaim_NotRecovered_WithinTimeout_RemainsInProgress()
    {
        var order = CreateOrder();
        var domainEvent = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var integration = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, domainEvent.OccurredAtUtc)
        {
            EventId = domainEvent.EventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = domainEvent.OccurredAtUtc
        };
        var outbox = _factory.Create(OrderCreatedIntegrationEvent.EventType, integration);
        OrdersContext.Orders.Add(order);
        OrdersContext.OutboxMessages.Add(outbox);
        await OrdersContext.SaveChangesAsync();

        var msg = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        await OrdersContext.OutboxMessages.Where(m => m.Id == msg.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.InProgress).SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow)); // fresh claim

        var recovered = await RecoverExpiredClaimsSQLiteAsync(OrdersContext, TimeSpan.FromMinutes(5));
        Assert.Equal(0, recovered);
        Assert.Equal(OutboxStatus.InProgress, (await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync()).Status);
    }

    // ------------------------------------------------ Retry & publication recovery

    [Fact]
    public async Task OutboxRetry_AfterBrokerRestored_PendingDispatched_ToPublished()
    {
        // Seed pending.
        var order = CreateOrder();
        var domainEvent = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var integration = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, domainEvent.OccurredAtUtc)
        {
            EventId = domainEvent.EventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = domainEvent.OccurredAtUtc
        };
        var outbox = _factory.Create(OrderCreatedIntegrationEvent.EventType, integration);
        OrdersContext.Orders.Add(order);
        OrdersContext.OutboxMessages.Add(outbox);
        await OrdersContext.SaveChangesAsync();

        // First attempt fails (retryable).
        Broker.SimulateConnectionOutage(times: 1);
        var pending = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        var failed = await SimulateDispatchOneAsync(OrdersContext, pending);
        Assert.False(failed);
        var retryable = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, retryable.Status);
        Assert.Equal(1, retryable.RetryCount);
        // Backoff in far future would block re-claim; force eligible for test.
        await OrdersContext.OutboxMessages.Where(m => m.Id == retryable.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RetryAfterUtc, DateTime.UtcNow.AddSeconds(-1)));

        // Clear fault, retry succeeds and marks Published.
        ResetFaults();
        var toRetry = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        var ok = await SimulateDispatchOneAsync(OrdersContext, toRetry);
        Assert.True(ok);
        var published = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Published, published.Status);
        Assert.NotNull(published.ProcessedAtUtc);
        Assert.Equal(1, published.RetryCount); // retry count preserved
    }

    [Fact]
    public async Task MultiplePending_AllDispatched_AfterBrokerRestored_ZeroLoss()
    {
        // Seed 3 business + outbox in one tx each — zero loss invariant.
        for (var i = 0; i < 3; i++)
        {
            var order = CreateOrder();
            var ev = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
            var integration = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, ev.OccurredAtUtc)
            {
                EventId = ev.EventId,
                CorrelationId = Guid.NewGuid(),
                OccurredAtUtc = ev.OccurredAtUtc
            };
            var outbox = _factory.Create(OrderCreatedIntegrationEvent.EventType, integration);
            OrdersContext.Orders.Add(order);
            OrdersContext.OutboxMessages.Add(outbox);
        }
        await OrdersContext.SaveChangesAsync();
        Assert.Equal(3, await OrdersContext.Orders.CountAsync());
        Assert.Equal(3, await OrdersContext.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Pending));

        // Simulate dispatcher batch: clear faults and dispatch all pending.
        ResetFaults();
        var pendings = await OrdersContext.OutboxMessages.Where(m => m.Status == OutboxStatus.Pending).AsNoTracking().ToListAsync();
        foreach (var msg in pendings)
        {
            var ok = await SimulateDispatchOneAsync(OrdersContext, msg);
            Assert.True(ok);
        }

        Assert.Equal(3, await OrdersContext.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Published));
        Assert.Equal(0, await OrdersContext.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Pending));
        Assert.Equal(3, await OrdersContext.Orders.CountAsync()); // business still 3
    }

    // ------------------------------------------------ Duplicate publication & idempotent downstream

    [Fact]
    public async Task DuplicatePublication_DownstreamIdempotent_NoDuplicateSideEffect()
    {
        // Publish same outbox payload twice; downstream Inventory consumer must deduplicate.
        var sku = new InventorySku($"INV-DUP-{Guid.NewGuid():N}"[..8].ToUpperInvariant());
        var eventId = Guid.NewGuid();
        var handler = "InventoryDuplicateHandler";

        // Simulate dispatch publishing same event twice (broker duplicate).
        var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent(
            Guid.NewGuid(), sku.Value, 3, DateTimeOffset.UtcNow.AddMinutes(15))
        {
            EventId = eventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        // First publish path
        var outbox = _factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent.EventType, integration);
        InventoryContext.OutboxMessages.Add(outbox);
        await InventoryContext.SaveChangesAsync();
        var pending = await InventoryContext.OutboxMessages.AsNoTracking().SingleAsync();
        var firstOk = await SimulateDispatchOneAsync(InventoryContext, pending);
        Assert.True(firstOk);

        // Downstream idempotent handling: first delivery creates business effect.
        var detector = new InventoryDuplicateDetector(InventoryContext);
        var store = new InventoryIdempotencyStore(InventoryContext);
        var processor = new InventoryIdempotentProcessor(InventoryContext, detector, store);
        var message = ProcessedMessage.Create(eventId, handler);

        var first = await processor.TryProcessAsync(message, async () =>
        {
            var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
            InventoryContext.InventoryItems.Add(item);
            await Task.CompletedTask;
        });
        Assert.False(first.IsDuplicate);
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(1, await InventoryContext.ProcessedMessages.CountAsync());

        // Duplicate dispatch: same EventId+Handler redelivered — must be idempotent.
        var duplicate = await processor.TryProcessAsync(message, async () =>
        {
            var dupSku = new InventorySku($"DUP-{Guid.NewGuid():N}"[..6].ToUpperInvariant());
            var dupItem = InventoryItem.Create(dupSku, 99, DateTimeOffset.UtcNow);
            InventoryContext.InventoryItems.Add(dupItem);
            await Task.CompletedTask;
        });
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync()); // no duplicate side-effect
        Assert.Equal(1, await InventoryContext.ProcessedMessages.CountAsync());
        Assert.DoesNotContain(await InventoryContext.InventoryItems.AsNoTracking().ToListAsync(), i => i.OnHandQuantity == 99);
    }

    [Fact]
    public async Task OutboxInvariant_CommittedBusinessEqualsPersistedOutbox_ZeroLoss()
    {
        // Zero message loss invariant: every committed business action has exactly one persisted outbox.
        var order = CreateOrder();
        var ev = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var integration = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, ev.OccurredAtUtc)
        {
            EventId = ev.EventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = ev.OccurredAtUtc
        };
        var outbox = _factory.Create(OrderCreatedIntegrationEvent.EventType, integration);

        OrdersContext.Orders.Add(order);
        OrdersContext.OutboxMessages.Add(outbox);
        await OrdersContext.SaveChangesAsync();

        // Invariant holds even though broker is down.
        Broker.SimulateConnectionOutage(times: 5);
        var pendingBefore = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, pendingBefore.Status);

        // Business committed, outbox persisted, publish failed — no loss.
        var ok = await SimulateDispatchOneAsync(OrdersContext, pendingBefore);
        Assert.False(ok);
        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
        Assert.Equal(1, await OrdersContext.OutboxMessages.CountAsync());

        // Eventually, broker restored → dispatched.
        ResetFaults();
        var retryable = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        await OrdersContext.OutboxMessages.Where(m => m.Id == retryable.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RetryAfterUtc, DateTime.UtcNow.AddSeconds(-1)));
        var toPublish = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        var published = await SimulateDispatchOneAsync(OrdersContext, toPublish);
        Assert.True(published);
        Assert.Equal(OutboxStatus.Published, (await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync()).Status);
    }

    // ------------------------------------------------ helpers

    private static Order CreateOrder()
    {
        var orderId = OrdersOrderId.Create();
        var customerId = new CustomerId(Guid.NewGuid());
        return Order.Place(
            orderId,
            customerId,
            OrderNumber.Create(),
            CreateAddress(),
            CreateAddress(),
            [
                new OrderLineItem(
                    OrderProductId.Create(),
                    OrderVariantId.Create(),
                    new OrderSku($"SKU-{Guid.NewGuid():N}"[..8].ToUpperInvariant()),
                    "Outbox Test Product",
                    new OrderMoney(10m, "USD"),
                    1),
            ],
            DateTimeOffset.UtcNow);
    }

    private static OrderAddress CreateAddress() =>
        new("Test Recipient", "1 Test St", null, "Test City", "Region", "1000", "US");

    /// <summary>
    /// SQLite-compatible dispatch simulation: claims via status check, publishes via BrokerFaultSimulator,
    /// then marks Published or retryable Pending with backoff. Returns true on success.
    /// Uses ExecuteUpdateAsync to avoid tracking conflicts on the immutable OutboxMessage record.
    /// </summary>
    private async Task<bool> SimulateDispatchOneAsync(Microsoft.EntityFrameworkCore.DbContext context, OutboxMessage snapshot)
    {
        var now = DateTime.UtcNow;
        var claimedRows = await context.Set<OutboxMessage>()
            .Where(m => m.Id == snapshot.Id && m.Status == OutboxStatus.Pending && (m.RetryAfterUtc == null || m.RetryAfterUtc <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.InProgress).SetProperty(m => m.ProcessedAtUtc, now));
        if (claimedRows == 0)
            return false;

        var tracked = await context.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == snapshot.Id);

        // Deserialize to integration event for publishing (supports OrderCreated / Inventory / Payment).
        var integrationEvent = DeserializeForPublish(tracked);
        if (integrationEvent is null)
        {
            await context.Set<OutboxMessage>().Where(m => m.Id == tracked.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Failed).SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow).SetProperty(m => m.Error, $"Unsupported outbox event type '{tracked.Type}'."));
            return false;
        }

        try
        {
            var published = await Broker.PublishAsync(tracked.Type, integrationEvent);
            if (!published)
            {
                var retryAfter = DateTime.UtcNow.AddSeconds(2);
                await context.Set<OutboxMessage>().Where(m => m.Id == tracked.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Pending).SetProperty(m => m.ProcessedAtUtc, (DateTime?)null).SetProperty(m => m.RetryCount, tracked.RetryCount + 1).SetProperty(m => m.RetryAfterUtc, retryAfter).SetProperty(m => m.Error, "The message broker did not confirm the publication."));
                return false;
            }

            await context.Set<OutboxMessage>().Where(m => m.Id == tracked.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Published).SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow));
            return true;
        }
        catch (Exception ex)
        {
            var retryAfter = DateTime.UtcNow.AddSeconds(2);
            await context.Set<OutboxMessage>().Where(m => m.Id == tracked.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Pending).SetProperty(m => m.ProcessedAtUtc, (DateTime?)null).SetProperty(m => m.RetryCount, tracked.RetryCount + 1).SetProperty(m => m.RetryAfterUtc, retryAfter).SetProperty(m => m.Error, ex.Message));
            return false;
        }
    }

    private static async Task<int> RecoverExpiredClaimsSQLiteAsync(Microsoft.EntityFrameworkCore.DbContext context, TimeSpan claimTimeout)
    {
        var cutoff = DateTime.UtcNow - claimTimeout;
        return await context.Set<OutboxMessage>()
            .Where(m => m.Status == OutboxStatus.InProgress && m.ProcessedAtUtc.HasValue && m.ProcessedAtUtc.Value < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Pending).SetProperty(m => m.ProcessedAtUtc, (DateTime?)null));
    }

    private static IIntegrationEvent? DeserializeForPublish(OutboxMessage message)
    {
        try
        {
            return message.Type switch
            {
                OrderCreatedIntegrationEvent.EventType =>
                    JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(message.Payload),
                OrderCancelledIntegrationEvent.EventType =>
                    JsonSerializer.Deserialize<OrderCancelledIntegrationEvent>(message.Payload),
                OrderConfirmedIntegrationEvent.EventType =>
                    JsonSerializer.Deserialize<OrderConfirmedIntegrationEvent>(message.Payload),
                FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent.EventType =>
                    JsonSerializer.Deserialize<FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent>(message.Payload),
                FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReleasedIntegrationEvent.EventType =>
                    JsonSerializer.Deserialize<FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReleasedIntegrationEvent>(message.Payload),
                FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent.EventType =>
                    JsonSerializer.Deserialize<FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent>(message.Payload),
                FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentFailedIntegrationEvent.EventType =>
                    JsonSerializer.Deserialize<FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentFailedIntegrationEvent>(message.Payload),
                _ => JsonSerializer.Deserialize<DummyEvent>(message.Payload)
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed record DummyEvent : IntegrationEvent;

    private sealed class RecordingPublisher : IRabbitMQPublisher
    {
        public List<(string Type, IIntegrationEvent Event)> Published { get; } = [];
        public Task<bool> PublishAsync(string eventType, IIntegrationEvent @event, CancellationToken cancellationToken = default)
        {
            Published.Add((eventType, @event));
            return Task.FromResult(true);
        }
    }
}
