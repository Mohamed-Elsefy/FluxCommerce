using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.Common.FaultInjection;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.IntegrationTests.Reliability;

/// <summary>
/// Phase 12.2 — Transaction &amp; Database Failure Testing. Verifies that database and transaction
/// failures never leave partial business state and that Outbox atomicity holds
/// (Business + Outbox commit or rollback together). Consumer DB failures are also verified to be
/// retryable without duplicate effects.
/// </summary>
[Trait("Category", "Reliability")]
public sealed class DatabaseFailureTests : ReliabilityTestBase
{
    private readonly IntegrationEventOutboxMessageFactory _outboxFactory = new();

    // ------------------------------------------------ Transaction rollback — business operations

    [Fact]
    public async Task Orders_TransactionRollback_OnTransientFailure_NoPartialOrderPersisted()
    {
        InjectDbSaveChangesFault(DbFaultKind.TransientFailure);

        var order = CreateOrder();
        OrdersContext.Orders.Add(order);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => OrdersContext.SaveChangesAsync());
        Assert.Contains("FaultInjection", ex.Message);

        Assert.Equal(0, await OrdersContext.Orders.CountAsync());
        Assert.Equal(0, await OrdersContext.OutboxMessages.CountAsync());

        // Recovery: fault cleared, retry succeeds and persists exactly one order.
        ResetFaults();
        OrdersContext.ChangeTracker.Clear();

        var retryOrder = CreateOrder();
        OrdersContext.Orders.Add(retryOrder);
        await OrdersContext.SaveChangesAsync();

        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
        var persisted = await OrdersContext.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(retryOrder.Id, persisted.Id);
    }

    [Fact]
    public async Task Orders_TransactionRollback_OnTimeout_NoPartialOrder()
    {
        InjectDbSaveChangesFault(DbFaultKind.Timeout);

        var order = CreateOrder();
        OrdersContext.Orders.Add(order);

        await Assert.ThrowsAsync<TimeoutException>(() => OrdersContext.SaveChangesAsync());

        Assert.Equal(0, await OrdersContext.Orders.CountAsync());
        Assert.Equal(0, await OrdersContext.CheckoutIdempotencies.CountAsync());

        ResetFaults();
        OrdersContext.ChangeTracker.Clear();
        var retry = CreateOrder();
        OrdersContext.Orders.Add(retry);
        await OrdersContext.SaveChangesAsync();
        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
    }

    [Fact]
    public async Task Inventory_TransactionRollback_OnTimeout_NoPartialItem()
    {
        InjectDbSaveChangesFault(DbFaultKind.Timeout);

        var sku = new InventorySku($"INV-TO-{Guid.NewGuid():N}"[..12].ToUpperInvariant());
        var item = InventoryItem.Create(sku, onHandQuantity: 10, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item);

        await Assert.ThrowsAsync<TimeoutException>(() => InventoryContext.SaveChangesAsync());

        Assert.Equal(0, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(0, await InventoryContext.OutboxMessages.CountAsync());

        ResetFaults();
        InventoryContext.ChangeTracker.Clear();
        var retrySku = new InventorySku($"INV-RT-{Guid.NewGuid():N}"[..12].ToUpperInvariant());
        var retryItem = InventoryItem.Create(retrySku, 10, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(retryItem);
        await InventoryContext.SaveChangesAsync();
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task Inventory_TransactionRollback_OnConnectionDrop_NoPartialReservation()
    {
        InjectDbSaveChangesFault(DbFaultKind.ConnectionDrop);

        var sku = new InventorySku($"INV-CD-{Guid.NewGuid():N}"[..12].ToUpperInvariant());
        var item = InventoryItem.Create(sku, 5, DateTimeOffset.UtcNow);
        item.Reserve(2, DateTimeOffset.UtcNow);
        var reservation = InventoryReservation.Hydrate(
            InventoryReservationId.Create(),
            new FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId(Guid.NewGuid()),
            sku,
            2,
            FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus.Pending,
            DateTimeOffset.UtcNow.AddMinutes(15),
            InventoryReservation.InitialVersion,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        InventoryContext.InventoryItems.Add(item);
        InventoryContext.InventoryReservations.Add(reservation);

        await Assert.ThrowsAsync<InvalidOperationException>(() => InventoryContext.SaveChangesAsync());

        // No partial state: either both or none.
        Assert.Equal(0, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(0, await InventoryContext.InventoryReservations.CountAsync());

        ResetFaults();
        InventoryContext.ChangeTracker.Clear();
        var sku2 = new InventorySku($"INV-CD2-{Guid.NewGuid():N}"[..4].ToUpperInvariant());
        var item2 = InventoryItem.Create(sku2, 5, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item2);
        await InventoryContext.SaveChangesAsync();
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task Payments_TransactionRollback_OnTransientFailure_NoPartialPayment()
    {
        InjectDbSaveChangesFault(DbFaultKind.TransientFailure);

        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 99.99m, "USD", Guid.NewGuid());
        PaymentsContext.Payments.Add(payment);

        await Assert.ThrowsAsync<InvalidOperationException>(() => PaymentsContext.SaveChangesAsync());
        Assert.Equal(0, await PaymentsContext.Payments.CountAsync());

        ResetFaults();
        PaymentsContext.ChangeTracker.Clear();
        var retry = new Payment(Guid.NewGuid(), Guid.NewGuid(), 49.50m, "USD", Guid.NewGuid());
        PaymentsContext.Payments.Add(retry);
        await PaymentsContext.SaveChangesAsync();
        Assert.Equal(1, await PaymentsContext.Payments.CountAsync());
    }

    // ------------------------------------------------ Outbox atomicity

    [Fact]
    public async Task Orders_OutboxAtomicity_BusinessAndOutbox_RolledBackTogether_OnFault()
    {
        // Arrange: business (Order) + outbox in same SaveChanges — the Phase 6 transaction boundary.
        var order = CreateOrder();
        var createdEvent = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var integrationEvent = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, createdEvent.OccurredAtUtc)
        {
            EventId = createdEvent.EventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = createdEvent.OccurredAtUtc
        };
        var outbox = _outboxFactory.Create(OrderCreatedIntegrationEvent.EventType, integrationEvent);

        OrdersContext.Orders.Add(order);
        OrdersContext.OutboxMessages.Add(outbox);

        InjectDbSaveChangesFault(DbFaultKind.Timeout);

        await Assert.ThrowsAsync<TimeoutException>(() => OrdersContext.SaveChangesAsync());

        // Assert: no orphan outbox without order, and no order without outbox.
        Assert.Equal(0, await OrdersContext.Orders.CountAsync());
        Assert.Equal(0, await OrdersContext.OutboxMessages.CountAsync());

        // No partial OrderLineItems either (owned collection rolled back) — verified via parent aggregate.
        Assert.Equal(0, await OrdersContext.Orders.SelectMany(o => o.LineItems).CountAsync());

        // Recovery: cleared fault persists both atomically.
        ResetFaults();
        OrdersContext.ChangeTracker.Clear();
        var retryOrder = CreateOrder();
        var retryCreated = retryOrder.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var retryIntegration = new OrderCreatedIntegrationEvent(retryOrder.Id.Value, retryOrder.CustomerId.Value, retryOrder.OrderNumber.Value, retryCreated.OccurredAtUtc)
        {
            EventId = retryCreated.EventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = retryCreated.OccurredAtUtc
        };
        var retryOutbox = _outboxFactory.Create(OrderCreatedIntegrationEvent.EventType, retryIntegration);
        OrdersContext.Orders.Add(retryOrder);
        OrdersContext.OutboxMessages.Add(retryOutbox);
        await OrdersContext.SaveChangesAsync();

        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
        Assert.Equal(1, await OrdersContext.OutboxMessages.CountAsync());
        var persistedOutbox = await OrdersContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(retryCreated.EventId, persistedOutbox.EventId);
        Assert.Equal(OutboxStatus.Pending, persistedOutbox.Status);
    }

    [Fact]
    public async Task Inventory_OutboxAtomicity_RolledBackTogether()
    {
        var sku = new InventorySku($"INV-OB-{Guid.NewGuid():N}"[..10].ToUpperInvariant());
        var item = InventoryItem.Create(sku, 20, DateTimeOffset.UtcNow);
        item.Reserve(5, DateTimeOffset.UtcNow);

        // Simulate inventory outbox pattern: business (item) + outbox in same tx.
        var integrationEvent = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent(
            Guid.NewGuid(), sku.Value, 5, DateTimeOffset.UtcNow.AddMinutes(15))
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        var outbox = _outboxFactory.Create(
            FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent.EventType,
            integrationEvent);

        InventoryContext.InventoryItems.Add(item);
        InventoryContext.OutboxMessages.Add(outbox);

        InjectDbSaveChangesFault(DbFaultKind.Deadlock);

        await Assert.ThrowsAsync<DbUpdateException>(() => InventoryContext.SaveChangesAsync());

        Assert.Equal(0, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(0, await InventoryContext.OutboxMessages.CountAsync());

        ResetFaults();
        InventoryContext.ChangeTracker.Clear();
        var sku2 = new InventorySku($"INV-OB2-{Guid.NewGuid():N}"[..10].ToUpperInvariant());
        var item2 = InventoryItem.Create(sku2, 10, DateTimeOffset.UtcNow);
        var ev2 = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent(
            Guid.NewGuid(), sku2.Value, 3, DateTimeOffset.UtcNow.AddMinutes(15))
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        var outbox2 = _outboxFactory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationRequestedIntegrationEvent.EventType, ev2);
        InventoryContext.InventoryItems.Add(item2);
        InventoryContext.OutboxMessages.Add(outbox2);
        await InventoryContext.SaveChangesAsync();
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(1, await InventoryContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Payments_OutboxAtomicity_RolledBackTogether()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 25m, "USD", Guid.NewGuid());
        var integrationEvent = new FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent(
            payment.OrderId, payment.PaymentId, "pi_test_atomic", 25m, "USD", "Succeeded", DateTimeOffset.UtcNow)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = payment.OrderId,
            OccurredAtUtc = DateTime.UtcNow
        };
        var outbox = _outboxFactory.Create(
            FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent.EventType,
            integrationEvent);

        PaymentsContext.Payments.Add(payment);
        PaymentsContext.OutboxMessages.Add(outbox);

        InjectDbSaveChangesFault(DbFaultKind.TransientFailure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PaymentsContext.SaveChangesAsync());

        Assert.Equal(0, await PaymentsContext.Payments.CountAsync());
        Assert.Equal(0, await PaymentsContext.OutboxMessages.CountAsync());

        ResetFaults();
        PaymentsContext.ChangeTracker.Clear();
        var retryPayment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 30m, "USD", Guid.NewGuid());
        var retryEvent = new FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent(
            retryPayment.OrderId, retryPayment.PaymentId, "pi_test_retry", 30m, "USD", "Succeeded", DateTimeOffset.UtcNow)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = retryPayment.OrderId,
            OccurredAtUtc = DateTime.UtcNow
        };
        var retryOutbox = _outboxFactory.Create(FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent.EventType, retryEvent);
        PaymentsContext.Payments.Add(retryPayment);
        PaymentsContext.OutboxMessages.Add(retryOutbox);
        await PaymentsContext.SaveChangesAsync();
        Assert.Equal(1, await PaymentsContext.Payments.CountAsync());
        Assert.Equal(1, await PaymentsContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task NoOrphanedOutboxMessages_AfterMultipleSequentialFailures()
    {
        // Multiple sequential failures must never leave orphaned outbox records.
        for (var i = 0; i < 3; i++)
        {
            var order = CreateOrder();
            var ev = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
            var integration = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, new DateTimeOffset(ev.OccurredAtUtc))
            {
                EventId = ev.EventId,
                CorrelationId = Guid.NewGuid(),
                OccurredAtUtc = ev.OccurredAtUtc
            };
            var outbox = _outboxFactory.Create(OrderCreatedIntegrationEvent.EventType, integration);
            OrdersContext.Orders.Add(order);
            OrdersContext.OutboxMessages.Add(outbox);
            InjectDbSaveChangesFault(DbFaultKind.TransientFailure);
            await Assert.ThrowsAsync<InvalidOperationException>(() => OrdersContext.SaveChangesAsync());
            OrdersContext.ChangeTracker.Clear();
            ResetFaults();
        }

        Assert.Equal(0, await OrdersContext.Orders.CountAsync());
        Assert.Equal(0, await OrdersContext.OutboxMessages.CountAsync());
        Assert.Equal(0, await OrdersContext.Orders.SelectMany(o => o.LineItems).CountAsync());

        // Final clean persist proves no leaked state affects next transaction.
        var final = CreateOrder();
        var finalEv = final.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var finalIntegration = new OrderCreatedIntegrationEvent(final.Id.Value, final.CustomerId.Value, final.OrderNumber.Value, new DateTimeOffset(finalEv.OccurredAtUtc))
        {
            EventId = finalEv.EventId,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = finalEv.OccurredAtUtc
        };
        var finalOutbox = _outboxFactory.Create(OrderCreatedIntegrationEvent.EventType, finalIntegration);
        OrdersContext.Orders.Add(final);
        OrdersContext.OutboxMessages.Add(finalOutbox);
        await OrdersContext.SaveChangesAsync();
        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
        Assert.Equal(1, await OrdersContext.OutboxMessages.CountAsync());
    }

    // ------------------------------------------------ Consumer database failures — retry/redelivery

    [Fact]
    public async Task InventoryConsumer_DbFailure_NotAcknowledged_RetrySucceeds_NoDuplicate()
    {
        // The idempotent processor: Business + ProcessedMessage in one tx.
        // Fault during SaveChanges must not create ProcessedMessage, so retry is eligible.
        var detector = new InventoryDuplicateDetector(InventoryContext);
        var store = new InventoryIdempotencyStore(InventoryContext);
        var processor = new InventoryIdempotentProcessor(InventoryContext, detector, store);

        var eventId = Guid.NewGuid();
        var handler = "TestInventoryHandler";
        var message = ProcessedMessage.Create(eventId, handler);
        var sku = new InventorySku($"INV-CONS-{Guid.NewGuid():N}"[..10].ToUpperInvariant());

        InjectDbSaveChangesFault(DbFaultKind.Timeout);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            processor.TryProcessAsync(message, async () =>
            {
                var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
                InventoryContext.InventoryItems.Add(item);
                await Task.CompletedTask;
            }));

        Assert.Contains("FaultInjection", ex.Message);
        Assert.Equal(0, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(0, await InventoryContext.ProcessedMessages.CountAsync());

        // Retry without fault succeeds — exactly one business effect.
        ResetFaults();
        InventoryContext.ChangeTracker.Clear();

        var result = await processor.TryProcessAsync(message, async () =>
        {
            var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
            InventoryContext.InventoryItems.Add(item);
            await Task.CompletedTask;
        });

        Assert.False(result.IsDuplicate);
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(1, await InventoryContext.ProcessedMessages.CountAsync());

        // Duplicate delivery after success is deduplicated, no second business effect.
        var duplicateResult = await processor.TryProcessAsync(message, async () =>
        {
            var dupSku = new InventorySku($"DUP-{Guid.NewGuid():N}"[..8].ToUpperInvariant());
            var dupItem = InventoryItem.Create(dupSku, 99, DateTimeOffset.UtcNow);
            InventoryContext.InventoryItems.Add(dupItem);
            await Task.CompletedTask;
        });

        Assert.True(duplicateResult.IsDuplicate);
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(1, await InventoryContext.ProcessedMessages.CountAsync());
        Assert.DoesNotContain(await InventoryContext.InventoryItems.AsNoTracking().ToListAsync(), i => i.OnHandQuantity == 99);
    }

    [Fact]
    public async Task OrdersConsumer_DbFailure_NotAcknowledged_RetrySucceeds_NoDuplicate()
    {
        var detector = new OrdersDuplicateDetector(OrdersContext);
        var store = new OrdersIdempotencyStore(OrdersContext);
        var processor = new OrdersIdempotentProcessor(OrdersContext, detector, store);

        var eventId = Guid.NewGuid();
        var handler = "TestOrdersHandler";
        var message = ProcessedMessage.Create(eventId, handler);

        InjectDbSaveChangesFault(DbFaultKind.TransientFailure);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.TryProcessAsync(message, async () =>
            {
                var order = CreateOrder();
                OrdersContext.Orders.Add(order);
                await Task.CompletedTask;
            }));

        Assert.Equal(0, await OrdersContext.Orders.CountAsync());
        Assert.Equal(0, await OrdersContext.ProcessedMessages.CountAsync());

        ResetFaults();
        OrdersContext.ChangeTracker.Clear();

        var result = await processor.TryProcessAsync(message, async () =>
        {
            var order = CreateOrder();
            OrdersContext.Orders.Add(order);
            await Task.CompletedTask;
        });

        Assert.False(result.IsDuplicate);
        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
        Assert.Equal(1, await OrdersContext.ProcessedMessages.CountAsync());

        var duplicateResult = await processor.TryProcessAsync(message, async () =>
        {
            var dupOrder = CreateOrder();
            OrdersContext.Orders.Add(dupOrder);
            await Task.CompletedTask;
        });

        Assert.True(duplicateResult.IsDuplicate);
        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
        Assert.Equal(1, await OrdersContext.ProcessedMessages.CountAsync());
    }

    [Fact]
    public async Task OrdersConsumer_DeadlockFailure_RetrySucceeds()
    {
        var detector = new OrdersDuplicateDetector(OrdersContext);
        var store = new OrdersIdempotencyStore(OrdersContext);
        var processor = new OrdersIdempotentProcessor(OrdersContext, detector, store);

        var eventId = Guid.NewGuid();
        var handler = "TestOrdersDeadlockHandler";
        var message = ProcessedMessage.Create(eventId, handler);

        // Deadlock throws DbUpdateException — processor catches it and checks for winner; if none, rethrows.
        // So the first attempt must bubble as DbUpdateException and leave no ProcessedMessage.
        InjectDbSaveChangesFault(DbFaultKind.Deadlock);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            processor.TryProcessAsync(message, async () =>
            {
                var order = CreateOrder();
                OrdersContext.Orders.Add(order);
                await Task.CompletedTask;
            }));

        Assert.Equal(0, await OrdersContext.Orders.CountAsync());
        Assert.Equal(0, await OrdersContext.ProcessedMessages.CountAsync());

        ResetFaults();
        OrdersContext.ChangeTracker.Clear();

        var retryResult = await processor.TryProcessAsync(message, async () =>
        {
            var order = CreateOrder();
            OrdersContext.Orders.Add(order);
            await Task.CompletedTask;
        });

        Assert.False(retryResult.IsDuplicate);
        Assert.Equal(1, await OrdersContext.Orders.CountAsync());
    }

    [Fact]
    public async Task InventoryConsumer_ConnectionDrop_RetrySucceeds()
    {
        var detector = new InventoryDuplicateDetector(InventoryContext);
        var store = new InventoryIdempotencyStore(InventoryContext);
        var processor = new InventoryIdempotentProcessor(InventoryContext, detector, store);

        var eventId = Guid.NewGuid();
        var message = ProcessedMessage.Create(eventId, "ConnDropHandler");
        var sku = new InventorySku($"INV-CND-{Guid.NewGuid():N}"[..10].ToUpperInvariant());

        InjectDbSaveChangesFault(DbFaultKind.ConnectionDrop);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.TryProcessAsync(message, async () =>
            {
                InventoryContext.InventoryItems.Add(InventoryItem.Create(sku, 5, DateTimeOffset.UtcNow));
                await Task.CompletedTask;
            }));

        Assert.Equal(0, await InventoryContext.InventoryItems.CountAsync());
        Assert.Equal(0, await InventoryContext.ProcessedMessages.CountAsync());

        ResetFaults();
        InventoryContext.ChangeTracker.Clear();

        var retry = await processor.TryProcessAsync(message, async () =>
        {
            InventoryContext.InventoryItems.Add(InventoryItem.Create(sku, 5, DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        });

        Assert.False(retry.IsDuplicate);
        Assert.Equal(1, await InventoryContext.InventoryItems.CountAsync());
    }

    [Fact]
    public async Task SagaState_NoPartialState_AfterOrdersSaveFailure()
    {
        // Saga state lives in Orders DB; verify no partial saga row survives a save failure.
        InjectDbSaveChangesFault(DbFaultKind.TransientFailure);

        var sagaId = Guid.NewGuid();
        var orderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId.Create();
        var saga = FluxCommerce.Modules.Orders.Domain.Sagas.OrderSaga.Start(sagaId, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        OrdersContext.OrderSagas.Add(saga);
        await Assert.ThrowsAsync<InvalidOperationException>(() => OrdersContext.SaveChangesAsync());

        Assert.Equal(0, await OrdersContext.OrderSagas.CountAsync());

        ResetFaults();
        OrdersContext.ChangeTracker.Clear();
        var sagaId2 = Guid.NewGuid();
        var orderId2 = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId.Create();
        var saga2 = FluxCommerce.Modules.Orders.Domain.Sagas.OrderSaga.Start(sagaId2, orderId2, Guid.NewGuid(), DateTimeOffset.UtcNow);
        OrdersContext.OrderSagas.Add(saga2);
        await OrdersContext.SaveChangesAsync();
        Assert.Equal(1, await OrdersContext.OrderSagas.CountAsync());
    }

    // ------------------------------------------------ helpers

    private static Order CreateOrder()
    {
        var orderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId.Create();
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
                    "DB Failure Test Product",
                    new OrderMoney(10m, "USD"),
                    1),
            ],
            DateTimeOffset.UtcNow);
    }

    private static OrderAddress CreateAddress() =>
        new("Test Recipient", "1 Test St", null, "Test City", "Region", "1000", "US");
}
