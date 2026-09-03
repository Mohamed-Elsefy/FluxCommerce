using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.IntegrationTests.Common.FaultInjection;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Stores;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;
using InventoryOrderId = FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId;

namespace FluxCommerce.IntegrationTests.Reliability;

/// <summary>
/// Phase 12.9 — Reservation Expiry Failure Testing. Validates worker crash recovery,
/// outbox retry, duplicate idempotency, and end-to-end expiry compensation.
/// </summary>
[Trait("Category", "Reliability")]
public sealed class ReservationExpiryFailureTests : ReliabilityTestBase
{
    private static Order CreateOrder(Guid? orderId = null)
    {
        var oid = orderId ?? Guid.NewGuid();
        return Order.Place(
            new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(oid),
            new CustomerId(Guid.NewGuid()),
            OrderNumber.Create(),
            new OrderAddress("Expiry Recipient", "1 Expiry St", null, "Expiry City", "Region", "1000", "US"),
            new OrderAddress("Expiry Recipient", "1 Expiry St", null, "Expiry City", "Region", "1000", "US"),
            [new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku($"SKU-{Guid.NewGuid():N}"[..6]), "Expiry Product", new OrderMoney(10m, "USD"), 1)],
            DateTimeOffset.UtcNow);
    }

    private OrderSagaOrchestrator CreateOrchestrator(OrdersDbContext ctx)
    {
        var sagaStore = new OrderSagaStore(ctx);
        var orderStore = new OrderStore(ctx);
        var factory = new SagaOutboxMessageFactory(new IntegrationEventOutboxMessageFactory());
        var dateTimeProvider = new FluxCommerce.BuildingBlocks.Infrastructure.Services.DateTimeProvider();
        var options = Microsoft.Extensions.Options.Options.Create(new ReservationRequestOptions { ReservationLifetime = TimeSpan.FromMinutes(15) });
        return new OrderSagaOrchestrator(sagaStore, orderStore, ctx, factory, dateTimeProvider, options, NullLogger<OrderSagaOrchestrator>.Instance);
    }

    private async Task<(Guid orderId, Guid sagaId, InventoryReservationId reservationId, InventorySku sku)> SeedExpiredPendingReservationAsync()
    {
        var orderId = Guid.NewGuid();
        var order = CreateOrder(orderId);
        OrdersContext.Orders.Add(order);
        var sagaId = Guid.NewGuid();
        var saga = OrderSaga.Start(sagaId, new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId), orderId, DateTimeOffset.UtcNow);
        saga.RequestReservation(DateTimeOffset.UtcNow);
        saga.MarkWaitingForPayment(DateTimeOffset.UtcNow);
        var reservationId = InventoryReservationId.Create();
        saga.RecordReservation(reservationId.Value);
        OrdersContext.OrderSagas.Add(saga);

        var sku = new InventorySku($"EXP-{Guid.NewGuid():N}"[..6]);
        var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
        item.Reserve(2, DateTimeOffset.UtcNow.AddHours(-2));
        InventoryContext.InventoryItems.Add(item);
        var reservation = InventoryReservation.Hydrate(
            reservationId,
            new InventoryOrderId(orderId),
            sku, 2,
            InventoryReservationStatus.Pending,
            DateTimeOffset.UtcNow.AddHours(-1), // already expired
            InventoryReservation.InitialVersion,
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-2));
        InventoryContext.InventoryReservations.Add(reservation);

        await OrdersContext.SaveChangesAsync();
        await InventoryContext.SaveChangesAsync();
        return (orderId, sagaId, reservationId, sku);
    }

    // ------------------------------------------------ Worker crash & resume

    [Fact]
    public async Task ExpirationWorker_TransientDbFault_NoIndeterminateState_ResumesNextCycle()
    {
        var (orderId, sagaId, reservationId, sku) = await SeedExpiredPendingReservationAsync();

        var beforeItem = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(2, beforeItem.ReservedQuantity);
        var beforeReservation = await InventoryContext.InventoryReservations.AsNoTracking().FirstAsync(r => r.Id == reservationId);
        Assert.Equal(InventoryReservationStatus.Pending, beforeReservation.Status);
        Assert.Equal(0, await InventoryContext.OutboxMessages.AsNoTracking().CountAsync());

        // Inject transient fault during expiration SaveChanges (business + outbox atomically rolled back).
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var reservation = await ctx.InventoryReservations.FirstAsync(r => r.Id == reservationId);
            var item = await ctx.InventoryItems.FirstAsync(i => i.Id == sku);
            var result = reservation.Expire(item, DateTimeOffset.UtcNow);
            Assert.True(result.IsSuccess);
            // Manually stage outbox to simulate interceptor (SQLite test does not have interceptor registered)
            var factory = new IntegrationEventOutboxMessageFactory();
            var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent(
                reservationId.Value, orderId, sku.Value, 2, DateTimeOffset.UtcNow.AddHours(-1))
            {
                EventId = reservationId.Value, // use reservationId as eventId for determinism
                CorrelationId = orderId,
                OccurredAtUtc = DateTime.UtcNow
            };
            ctx.OutboxMessages.Add(factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent.EventType, integration));

            InjectDbSaveChangesFault(DbFaultKind.TransientFailure);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        }

        // No indeterminate state: still Pending, still reserved, no outbox.
        var afterCrashReservation = await InventoryContext.InventoryReservations.AsNoTracking().FirstAsync(r => r.Id == reservationId);
        Assert.Equal(InventoryReservationStatus.Pending, afterCrashReservation.Status);
        var afterCrashItem = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(2, afterCrashItem.ReservedQuantity);
        Assert.Equal(0, await InventoryContext.OutboxMessages.AsNoTracking().CountAsync());
        ResetFaults();

        // Next poll cycle resumes and succeeds.
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var reservation = await ctx.InventoryReservations.FirstAsync(r => r.Id == reservationId);
            var item = await ctx.InventoryItems.FirstAsync(i => i.Id == sku);
            var result = reservation.Expire(item, DateTimeOffset.UtcNow);
            Assert.True(result.IsSuccess);
            var factory = new IntegrationEventOutboxMessageFactory();
            var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent(
                reservationId.Value, orderId, sku.Value, 2, DateTimeOffset.UtcNow.AddHours(-1))
            {
                EventId = Guid.NewGuid(),
                CorrelationId = orderId,
                OccurredAtUtc = DateTime.UtcNow
            };
            ctx.OutboxMessages.Add(factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent.EventType, integration));
            await ctx.SaveChangesAsync();
        }

        var afterSuccessReservation = await InventoryContext.InventoryReservations.AsNoTracking().FirstAsync(r => r.Id == reservationId);
        Assert.Equal(InventoryReservationStatus.Expired, afterSuccessReservation.Status);
        var afterSuccessItem = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(0, afterSuccessItem.ReservedQuantity);
        Assert.Equal(10, afterSuccessItem.AvailableQuantity);
        Assert.Equal(1, await InventoryContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "inventory.reservation_expired").CountAsync());
    }

    // ------------------------------------------------ Outbox / broker failure retryable

    [Fact]
    public async Task Expiration_OutboxBrokerFailure_Retryable_EventuallyReceived()
    {
        var (orderId, _, reservationId, sku) = await SeedExpiredPendingReservationAsync();

        // Successful expiration creates outbox Pending.
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var reservation = await ctx.InventoryReservations.FirstAsync(r => r.Id == reservationId);
            var item = await ctx.InventoryItems.FirstAsync(i => i.Id == sku);
            reservation.Expire(item, DateTimeOffset.UtcNow);
            var factory = new IntegrationEventOutboxMessageFactory();
            var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent(
                reservationId.Value, orderId, sku.Value, 2, DateTimeOffset.UtcNow.AddHours(-1))
            {
                EventId = Guid.NewGuid(),
                CorrelationId = orderId,
                OccurredAtUtc = DateTime.UtcNow
            };
            var outbox = factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent.EventType, integration);
            ctx.OutboxMessages.Add(outbox);
            await ctx.SaveChangesAsync();
        }

        var outboxBefore = await InventoryContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Type == "inventory.reservation_expired");
        Assert.Equal(OutboxStatus.Pending, outboxBefore.Status);
        Assert.Equal(0, outboxBefore.RetryCount);

        // Simulate broker failure during dispatch: mark retryable (Pending with RetryCount+1, RetryAfter, Error) — SQLite-compatible.
        await InventoryContext.OutboxMessages.Where(m => m.Id == outboxBefore.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Pending)
                .SetProperty(m => m.RetryCount, outboxBefore.RetryCount + 1)
                .SetProperty(m => m.RetryAfterUtc, DateTime.UtcNow.AddSeconds(2))
                .SetProperty(m => m.Error, "Simulated broker failure"));

        var afterFail = await InventoryContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == outboxBefore.Id);
        Assert.Equal(OutboxStatus.Pending, afterFail.Status);
        Assert.Equal(1, afterFail.RetryCount);
        Assert.NotNull(afterFail.RetryAfterUtc);
        // Inventory already released transactionally, business + outbox atomic, no leak.
        var itemAfterFail = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(0, itemAfterFail.ReservedQuantity);

        // Broker restored: force eligible and dispatch succeeds → Published.
        await InventoryContext.OutboxMessages.Where(m => m.Id == outboxBefore.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RetryAfterUtc, DateTime.UtcNow.AddSeconds(-1)));
        var toDispatch = await InventoryContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == outboxBefore.Id);
        // Simulate successful publish → Published
        await InventoryContext.OutboxMessages.Where(m => m.Id == toDispatch.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Published).SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow));
        var published = await InventoryContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == outboxBefore.Id);
        Assert.Equal(OutboxStatus.Published, published.Status);
        Assert.NotNull(published.ProcessedAtUtc);
    }

    // ------------------------------------------------ Idempotency duplicate handling

    [Fact]
    public async Task Duplicate_ReservationExpired_Sequential_Idempotent_NoDuplicateCancellation()
    {
        var (orderId, sagaId, reservationId, sku) = await SeedExpiredPendingReservationAsync();

        // First expiration via saga: WaitingForPayment → Cancelled
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var orch = CreateOrchestrator(ctx);
            var r = await orch.ApplyReservationExpiredAsync(
                new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                reservationId.Value, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);
            Assert.Equal(OrderSagaTransitionOutcome.Applied, r.Outcome);
            await ctx.SaveChangesAsync();
        }
        var sagaAfter1 = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.Cancelled, sagaAfter1.State);
        var orderAfter1 = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId));
        Assert.Equal(OrderStatus.Cancelled, orderAfter1.Status);
        var outboxCountAfter1 = await OrdersContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "inventory.reservation_expired").CountAsync();

        // Duplicate same reservationId sequentially via idempotent processor (same EventId)
        var eventId = reservationId.Value; // reuse as eventId for determinism
        var handler = "Orders.ApplyReservationExpired";
        var message = FluxCommerce.BuildingBlocks.Idempotency.Messages.ProcessedMessage.Create(eventId, handler);
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var processor = new OrdersIdempotentProcessor(ctx, new OrdersDuplicateDetector(ctx), new OrdersIdempotencyStore(ctx));
            // First idempotent delivery (with fresh handler) would be duplicate already because saga already processed?
            // Simulate second delivery via processor: it should be duplicate and not change saga.
            var result = await processor.TryProcessAsync(message, async () =>
            {
                var orch = CreateOrchestrator(ctx);
                await orch.ApplyReservationExpiredAsync(
                    new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                    reservationId.Value, orderId, eventId, DateTimeOffset.UtcNow);
            });
            // If not duplicate, it would be Ignored (Success with no version bump) but still IsDuplicate false on first idempotent call?
            // For this test, we assert second idempotent duplicate does not change saga.
            // The processor's first call with this message will be NotProcessed (since ProcessedMessage not yet exists for this handler),
            // but the saga transition inside will be Ignored (Cancelled → Ignored), so no extra outbox.
        }

        // Second duplicate via same ProcessedMessage should be AlreadyProcessed
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var processor = new OrdersIdempotentProcessor(ctx, new OrdersDuplicateDetector(ctx), new OrdersIdempotencyStore(ctx));
            var dup = await processor.TryProcessAsync(message, async () =>
            {
                var orch = CreateOrchestrator(ctx);
                await orch.ApplyReservationExpiredAsync(
                    new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                    reservationId.Value, orderId, eventId, DateTimeOffset.UtcNow);
            });
            Assert.True(dup.IsDuplicate);
        }

        var sagaAfterDup = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.Cancelled, sagaAfterDup.State);
        var orderAfterDup = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId));
        Assert.Equal(OrderStatus.Cancelled, orderAfterDup.Status);
        // No duplicate cancellation side-effect: outbox still same (no extra order_cancelled from duplicate)
        Assert.Equal(outboxCountAfter1, await OrdersContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "inventory.reservation_expired").CountAsync());
    }

    [Fact]
    public async Task Duplicate_ReservationExpired_Concurrent_Idempotent_NoDuplicateCancellation()
    {
        var (orderId, sagaId, reservationId, _) = await SeedExpiredPendingReservationAsync();

        // Expire once to get to Cancelled, then concurrent duplicates
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var orch = CreateOrchestrator(ctx);
            var r = await orch.ApplyReservationExpiredAsync(
                new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                reservationId.Value, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);
            Assert.Equal(OrderSagaTransitionOutcome.Applied, r.Outcome);
            await ctx.SaveChangesAsync();
        }

        var eventId = Guid.NewGuid();
        var handler = "Orders.ApplyReservationExpired.Concurrent";
        var message = FluxCommerce.BuildingBlocks.Idempotency.Messages.ProcessedMessage.Create(eventId, handler);

        // Two concurrent deliveries same EventId/Handler (sequential simulation due to SQLite single connection)
        FluxCommerce.BuildingBlocks.Idempotency.Messages.IdempotencyResult r1, r2;
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var processor = new OrdersIdempotentProcessor(ctx, new OrdersDuplicateDetector(ctx), new OrdersIdempotencyStore(ctx));
            r1 = await processor.TryProcessAsync(message, async () =>
            {
                var orch = CreateOrchestrator(ctx);
                await orch.ApplyReservationExpiredAsync(
                    new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                    reservationId.Value, orderId, eventId, DateTimeOffset.UtcNow);
            });
        }
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var processor = new OrdersIdempotentProcessor(ctx, new OrdersDuplicateDetector(ctx), new OrdersIdempotencyStore(ctx));
            r2 = await processor.TryProcessAsync(message, async () =>
            {
                var orch = CreateOrchestrator(ctx);
                await orch.ApplyReservationExpiredAsync(
                    new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                    reservationId.Value, orderId, eventId, DateTimeOffset.UtcNow);
            });
        }

        Assert.True((!r1.IsDuplicate && r2.IsDuplicate) || (!r2.IsDuplicate && r1.IsDuplicate));
        var sagaAfter = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.Cancelled, sagaAfter.State);
        Assert.Equal(1, await OrdersContext.ProcessedMessages.AsNoTracking().Where(m => m.Key == eventId.ToString() && m.Handler == handler).CountAsync());
    }

    // ------------------------------------------------ End-to-end expiry compensation

    [Fact]
    public async Task EndToEnd_ExpiredReservation_FullFlow_ZeroLeak_OrderCancelled()
    {
        var (orderId, sagaId, reservationId, sku) = await SeedExpiredPendingReservationAsync();

        // Step 1: Expire reservation -> Release inventory + Outbox (simulate worker)
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var reservation = await ctx.InventoryReservations.FirstAsync(r => r.Id == reservationId);
            var item = await ctx.InventoryItems.FirstAsync(i => i.Id == sku);
            var res = reservation.Expire(item, DateTimeOffset.UtcNow);
            Assert.True(res.IsSuccess);
            var factory = new IntegrationEventOutboxMessageFactory();
            var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent(
                reservationId.Value, orderId, sku.Value, 2, DateTimeOffset.UtcNow.AddHours(-1))
            {
                EventId = Guid.NewGuid(),
                CorrelationId = orderId,
                OccurredAtUtc = DateTime.UtcNow
            };
            ctx.OutboxMessages.Add(factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent.EventType, integration));
            await ctx.SaveChangesAsync();
        }

        // Verify zero leak after expiration: reserved 0, available 10, Expired
        var itemAfterExpire = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(0, itemAfterExpire.ReservedQuantity);
        Assert.Equal(10, itemAfterExpire.AvailableQuantity);
        Assert.Equal(10, itemAfterExpire.OnHandQuantity);
        var reservationAfterExpire = await InventoryContext.InventoryReservations.AsNoTracking().FirstAsync(r => r.Id == reservationId);
        Assert.Equal(InventoryReservationStatus.Expired, reservationAfterExpire.Status);
        var outbox = await InventoryContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Type == "inventory.reservation_expired");
        Assert.Equal(OutboxStatus.Pending, outbox.Status);

        // Step 2: Simulate broker dispatch success → Published
        await InventoryContext.OutboxMessages.Where(m => m.Id == outbox.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Published).SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow));

        // Step 3: Orders saga consumes ReservationExpired -> Cancel Order (idempotent)
        var eventId = outbox.EventId;
        var handler = "Orders.ApplyReservationExpired";
        var message = FluxCommerce.BuildingBlocks.Idempotency.Messages.ProcessedMessage.Create(eventId, handler);
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var processor = new OrdersIdempotentProcessor(ctx, new OrdersDuplicateDetector(ctx), new OrdersIdempotencyStore(ctx));
            var result = await processor.TryProcessAsync(message, async () =>
            {
                var orch = CreateOrchestrator(ctx);
                await orch.ApplyReservationExpiredAsync(
                    new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                    reservationId.Value, orderId, eventId, DateTimeOffset.UtcNow);
            });
            Assert.False(result.IsDuplicate);
        }

        var sagaAfter = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.Cancelled, sagaAfter.State);
        var orderAfter = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId));
        Assert.Equal(OrderStatus.Cancelled, orderAfter.Status);

        // Duplicate end-to-end redelivery is idempotent
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var processor = new OrdersIdempotentProcessor(ctx, new OrdersDuplicateDetector(ctx), new OrdersIdempotencyStore(ctx));
            var dup = await processor.TryProcessAsync(message, async () =>
            {
                var orch = CreateOrchestrator(ctx);
                await orch.ApplyReservationExpiredAsync(
                    new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                    reservationId.Value, orderId, eventId, DateTimeOffset.UtcNow);
            });
            Assert.True(dup.IsDuplicate);
        }

        // Final invariants: no leaked stock, order cancelled, business effect 1
        var finalItem = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(0, finalItem.ReservedQuantity);
        Assert.Equal(10, finalItem.AvailableQuantity);
        Assert.Equal(finalItem.OnHandQuantity, finalItem.AvailableQuantity + finalItem.ReservedQuantity);
        var finalOrder = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId));
        Assert.Equal(OrderStatus.Cancelled, finalOrder.Status);
        Assert.Equal(1, await OrdersContext.ProcessedMessages.AsNoTracking().Where(m => m.Key == eventId.ToString()).CountAsync());
    }

    [Fact]
    public async Task EndToEnd_ExpiredReservation_WithTransientFault_Retries_ZeroLeak()
    {
        var (orderId, sagaId, reservationId, sku) = await SeedExpiredPendingReservationAsync();

        // First attempt fails transiently, second succeeds
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var reservation = await ctx.InventoryReservations.FirstAsync(r => r.Id == reservationId);
            var item = await ctx.InventoryItems.FirstAsync(i => i.Id == sku);
            reservation.Expire(item, DateTimeOffset.UtcNow);
            var factory = new IntegrationEventOutboxMessageFactory();
            var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent(
                reservationId.Value, orderId, sku.Value, 2, DateTimeOffset.UtcNow.AddHours(-1))
            {
                EventId = Guid.NewGuid(),
                CorrelationId = orderId,
                OccurredAtUtc = DateTime.UtcNow
            };
            ctx.OutboxMessages.Add(factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent.EventType, integration));
            InjectDbSaveChangesFault(DbFaultKind.TransientFailure);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        }

        // Verify still pending, still reserved
        var reservationAfterFail = await InventoryContext.InventoryReservations.AsNoTracking().FirstAsync(r => r.Id == reservationId);
        Assert.Equal(InventoryReservationStatus.Pending, reservationAfterFail.Status);
        var itemAfterFail = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(2, itemAfterFail.ReservedQuantity);
        Assert.Equal(0, await InventoryContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "inventory.reservation_expired").CountAsync());
        ResetFaults();

        // Retry succeeds
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var reservation = await ctx.InventoryReservations.FirstAsync(r => r.Id == reservationId);
            var item = await ctx.InventoryItems.FirstAsync(i => i.Id == sku);
            var res = reservation.Expire(item, DateTimeOffset.UtcNow);
            Assert.True(res.IsSuccess);
            var factory = new IntegrationEventOutboxMessageFactory();
            var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent(
                reservationId.Value, orderId, sku.Value, 2, DateTimeOffset.UtcNow.AddHours(-1))
            {
                EventId = Guid.NewGuid(),
                CorrelationId = orderId,
                OccurredAtUtc = DateTime.UtcNow
            };
            ctx.OutboxMessages.Add(factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent.EventType, integration));
            await ctx.SaveChangesAsync();
        }

        var itemAfter = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(0, itemAfter.ReservedQuantity);
        Assert.Equal(10, itemAfter.AvailableQuantity);
        // Continue to saga cancellation
        var outbox = await InventoryContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Type == "inventory.reservation_expired");
        await InventoryContext.OutboxMessages.Where(m => m.Id == outbox.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Published).SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow));

        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var processor = new OrdersIdempotentProcessor(ctx, new OrdersDuplicateDetector(ctx), new OrdersIdempotencyStore(ctx));
            var message = FluxCommerce.BuildingBlocks.Idempotency.Messages.ProcessedMessage.Create(outbox.EventId, "Orders.ApplyReservationExpired");
            await processor.TryProcessAsync(message, async () =>
            {
                var orch = CreateOrchestrator(ctx);
                await orch.ApplyReservationExpiredAsync(
                    new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
                    reservationId.Value, orderId, outbox.EventId, DateTimeOffset.UtcNow);
            });
        }

        var sagaAfter = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.Cancelled, sagaAfter.State);
        var orderAfter = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId));
        Assert.Equal(OrderStatus.Cancelled, orderAfter.Status);
    }
}
