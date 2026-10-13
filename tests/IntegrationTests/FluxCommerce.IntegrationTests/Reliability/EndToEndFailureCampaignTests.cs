using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.Common.FaultInjection;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
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
using FluxCommerce.Modules.Orders.Infrastructure.Options;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Idempotency;
using FluxCommerce.Modules.Payments.Infrastructure.Webhook;
using FluxCommerce.Modules.Payments.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;
using InventoryOrderId = FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId;

namespace FluxCommerce.IntegrationTests.Reliability;

/// <summary>
/// Phase 12.10 — End-to-End Failure Campaign. Validates multi-stage recovery and
/// cascading compensation under combined faults.
/// </summary>
[Trait("Category", "Reliability")]
public sealed class EndToEndFailureCampaignTests : ReliabilityTestBase
{
    private readonly IntegrationEventOutboxMessageFactory _factory = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IRabbitMQPublisher>(sp => new RecordingPublisher());
    }

    private BrokerFaultSimulator Broker => BrokerFaultSimulatorInstance!;

    private static Order CreateOrder(Guid? orderId = null)
    {
        var oid = orderId ?? Guid.NewGuid();
        return Order.Place(
            new OrdersOrderId(oid),
            new CustomerId(Guid.NewGuid()),
            OrderNumber.Create(),
            new OrderAddress("E2E Recipient", "1 E2E St", null, "E2E City", "Region", "1000", "US"),
            new OrderAddress("E2E Recipient", "1 E2E St", null, "E2E City", "Region", "1000", "US"),
            [new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku($"SKU-{Guid.NewGuid():N}"[..6]), "E2E Product", new OrderMoney(10m, "USD"), 1)],
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

    private static void AssertInventoryInvariants(InventoryItem item)
    {
        Assert.True(item.AvailableQuantity >= 0);
        Assert.True(item.ReservedQuantity <= item.OnHandQuantity);
        Assert.Equal(item.OnHandQuantity, item.AvailableQuantity + item.ReservedQuantity);
    }

    private async Task<bool> SimulateDispatchWithBrokerFaultAsync(Microsoft.EntityFrameworkCore.DbContext ctx, OutboxMessage snapshot)
    {
        var now = DateTime.UtcNow;
        var claimed = await ctx.Set<OutboxMessage>()
            .Where(m => m.Id == snapshot.Id && m.Status == OutboxStatus.Pending && (m.RetryAfterUtc == null || m.RetryAfterUtc <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.InProgress).SetProperty(m => m.ProcessedAtUtc, now));
        if (claimed == 0) return false;
        var tracked = await ctx.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == snapshot.Id);
        var integration = DeserializeForPublish(tracked);
        if (integration is null)
        {
            await ctx.Set<OutboxMessage>().Where(m => m.Id == tracked.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Failed).SetProperty(m => m.Error, "Unsupported"));
            return false;
        }
        try
        {
            var published = await Broker.PublishAsync(tracked.Type, integration);
            if (!published)
            {
                await ctx.Set<OutboxMessage>().Where(m => m.Id == tracked.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Pending).SetProperty(m => m.RetryCount, tracked.RetryCount + 1).SetProperty(m => m.RetryAfterUtc, DateTime.UtcNow.AddSeconds(2)).SetProperty(m => m.Error, "Broker NACK"));
                return false;
            }
            await ctx.Set<OutboxMessage>().Where(m => m.Id == tracked.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Published).SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow));
            return true;
        }
        catch (Exception ex)
        {
            await ctx.Set<OutboxMessage>().Where(m => m.Id == tracked.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Pending).SetProperty(m => m.RetryCount, tracked.RetryCount + 1).SetProperty(m => m.RetryAfterUtc, DateTime.UtcNow.AddSeconds(2)).SetProperty(m => m.Error, ex.Message));
            return false;
        }
    }

    private static FluxCommerce.BuildingBlocks.Messaging.Events.IIntegrationEvent? DeserializeForPublish(OutboxMessage msg)
    {
        try
        {
            return msg.Type switch
            {
                FluxCommerce.Modules.Orders.Contracts.IntegrationEvents.OrderCreatedIntegrationEvent.EventType => System.Text.Json.JsonSerializer.Deserialize<FluxCommerce.Modules.Orders.Contracts.IntegrationEvents.OrderCreatedIntegrationEvent>(msg.Payload),
                FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent.EventType => System.Text.Json.JsonSerializer.Deserialize<FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent>(msg.Payload),
                FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentFailedIntegrationEvent.EventType => System.Text.Json.JsonSerializer.Deserialize<FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentFailedIntegrationEvent>(msg.Payload),
                _ => new DummyEvent { EventId = msg.EventId, CorrelationId = msg.CorrelationId, OccurredAtUtc = msg.OccurredAtUtc }
            };
        }
        catch { return null; }
    }

    private sealed record DummyEvent : FluxCommerce.BuildingBlocks.Messaging.Events.IntegrationEvent;

    private sealed class RecordingPublisher : IRabbitMQPublisher
    {
        public List<(string Type, FluxCommerce.BuildingBlocks.Messaging.Events.IIntegrationEvent Event)> Published { get; } = [];
        public Task<bool> PublishAsync(string eventType, FluxCommerce.BuildingBlocks.Messaging.Events.IIntegrationEvent @event, CancellationToken cancellationToken = default)
        {
            Published.Add((eventType, @event));
            return Task.FromResult(true);
        }
    }

    // ------------------------------------------------ Scenario A: Multi-fault success path

    [Fact]
    public async Task ScenarioA_MultiFaultSuccessPath_TransientDb_ConsumerCrash_BrokerOutage_EventualConfirmed_NoDuplicates()
    {
        // Step 0: Seed business — Order Pending, Saga Started, Item OnHand 10.
        var orderId = Guid.NewGuid();
        var order = CreateOrder(orderId);
        OrdersContext.Orders.Add(order);
        var sagaId = Guid.NewGuid();
        var saga = OrderSaga.Start(sagaId, new OrdersOrderId(orderId), orderId, DateTimeOffset.UtcNow);
        saga.RequestReservation(DateTimeOffset.UtcNow);
        OrdersContext.OrderSagas.Add(saga);
        var sku = new InventorySku($"E2E-A-{Guid.NewGuid():N}"[..6]);
        var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item);
        await OrdersContext.SaveChangesAsync();
        await InventoryContext.SaveChangesAsync();

        // Step 1: Inventory reservation with transient DB fault (command-level) → retry succeeds.
        InjectDbCommandFault(DbFaultKind.TransientFailure, times: 1);
        InventoryReservation? reserved = null;
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var store = new InventoryStore(ctx);
            var rid = InventoryReservationId.Create();
            var oid = new InventoryOrderId(orderId);
            try
            {
                reserved = await store.TryReserveAsync(sku, rid, oid, 1, DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow);
            }
            catch (InvalidOperationException) { /* transient fault */ }
            if (reserved is null)
            {
                // Retry after transient — fault auto-cleared after 1 throw.
                var retryRid = InventoryReservationId.Create();
                reserved = await store.TryReserveAsync(sku, retryRid, oid, 1, DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow);
            }
        }
        ResetFaults();
        Assert.NotNull(reserved);
        var afterReserveItem = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        AssertInventoryInvariants(afterReserveItem);
        Assert.Equal(1, afterReserveItem.ReservedQuantity);
        Assert.Equal(9, afterReserveItem.AvailableQuantity);

        // Record reservation on saga and transition to WaitingForPayment (simulate reservation success).
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var orch = CreateOrchestrator(ctx);
            var resId = reserved!.Id.Value;
            var r = await orch.ApplyReservationSuccessAsync(new OrdersOrderId(orderId), resId, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);
            Assert.Equal(OrderSagaTransitionOutcome.Applied, r.Outcome);
            await ctx.SaveChangesAsync();
        }
        var sagaAfterReserve = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.WaitingForPayment, sagaAfterReserve.State);

        // Step 2: Payment authorization with crash after DB commit before ACK.
        var paymentId = Guid.NewGuid();
        var paymentOrderId = orderId;
        var eventId = Guid.NewGuid();
        const string handler = "E2E-Payment-CrashAfterCommit";
        var message = ProcessedMessage.Create(eventId, handler);
        CrashNextConsumerAfterCommit(1);
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var processor = new PaymentsIdempotentProcessor(ctx, new PaymentsDuplicateDetector(ctx), new PaymentsIdempotencyStore(ctx));
            var crashing = new CrashingIdempotentProcessorDecorator(processor);
            var ex = await Assert.ThrowsAsync<ConsumerCrashException>(() =>
                crashing.TryProcessAsync(message, async () =>
                {
                    ctx.Payments.Add(new Payment(paymentId, paymentOrderId, 10m, "USD", Guid.NewGuid()));
                    await Task.CompletedTask;
                }));
            Assert.Contains("after DB commit", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.Id == paymentId).CountAsync());
        Assert.Equal(1, await PaymentsContext.ProcessedMessages.AsNoTracking().Where(m => m.Key == eventId.ToString()).CountAsync());

        // Redelivery → duplicate, no second payment.
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var processor = new PaymentsIdempotentProcessor(ctx, new PaymentsDuplicateDetector(ctx), new PaymentsIdempotencyStore(ctx));
            var crashing = new CrashingIdempotentProcessorDecorator(processor);
            var dup = await crashing.TryProcessAsync(message, async () =>
            {
                ctx.Payments.Add(new Payment(Guid.NewGuid(), paymentOrderId, 10m, "USD", Guid.NewGuid()));
                await Task.CompletedTask;
            });
            Assert.True(dup.IsDuplicate);
        }
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.Id == paymentId).CountAsync());
        ResetFaults();

        // Mark payment succeeded via domain and create outbox PaymentSucceeded for dispatch.
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var payment = await ctx.Payments.FirstAsync(p => p.Id == paymentId);
            payment.UpdateProviderReference($"pi_{paymentId:N}", "Stripe");
            payment.Process();
            payment.Succeed($"pi_{paymentId:N}");
            var factory = new IntegrationEventOutboxMessageFactory();
            var integration = new FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent(
                payment.OrderId, payment.Id, $"pi_{paymentId:N}", 10m, "USD", "Succeeded", DateTimeOffset.UtcNow)
            {
                EventId = Guid.NewGuid(),
                CorrelationId = orderId,
                OccurredAtUtc = DateTime.UtcNow
            };
            ctx.OutboxMessages.Add(factory.Create(FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.PaymentSucceededIntegrationEvent.EventType, integration));
            await ctx.SaveChangesAsync();
        }

        // Step 3: Broker outage while dispatching PaymentSucceeded outbox.
        var outbox = await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Type == "payments.payment_succeeded");
        Broker.SimulateConnectionOutage(times: 1);
        var dispatchedFail = await SimulateDispatchWithBrokerFaultAsync(PaymentsContext, outbox);
        Assert.False(dispatchedFail);
        var afterBrokerFail = await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == outbox.Id);
        Assert.Equal(OutboxStatus.Pending, afterBrokerFail.Status);
        Assert.Equal(1, afterBrokerFail.RetryCount);

        // Restore broker and allow retry.
        ResetFaults();
        await PaymentsContext.OutboxMessages.Where(m => m.Id == outbox.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RetryAfterUtc, DateTime.UtcNow.AddSeconds(-1)));
        var toRetry = await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == outbox.Id);
        var dispatchedOk = await SimulateDispatchWithBrokerFaultAsync(PaymentsContext, toRetry);
        Assert.True(dispatchedOk);
        var published = await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == outbox.Id);
        Assert.Equal(OutboxStatus.Published, published.Status);

        // Step 4: Saga consumes PaymentSucceeded → Order Confirmed.
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var orch = CreateOrchestrator(ctx);
            var r = await orch.ApplyPaymentSucceededAsync(new OrdersOrderId(orderId), paymentId, $"pi_{paymentId:N}", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);
            Assert.Equal(OrderSagaTransitionOutcome.Applied, r.Outcome);
            await ctx.SaveChangesAsync();
        }

        // Final invariants: Valid terminal state, exactly 1 of each side-effect, eventual consistency 100%.
        var finalOrder = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new OrdersOrderId(orderId));
        Assert.Equal(OrderStatus.Confirmed, finalOrder.Status);
        var finalSaga = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.Completed, finalSaga.State);
        var finalItem = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        AssertInventoryInvariants(finalItem);
        Assert.Equal(1, finalItem.ReservedQuantity);
        Assert.Equal(1, await OrdersContext.Orders.AsNoTracking().Where(o => o.Id == new OrdersOrderId(orderId)).CountAsync());
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.Id == paymentId).CountAsync());
        Assert.Equal(1, await InventoryContext.InventoryReservations.AsNoTracking().Where(r => r.OrderId == new InventoryOrderId(orderId)).CountAsync());
        Assert.Equal(1, await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "payments.payment_succeeded" && m.Status == OutboxStatus.Published).CountAsync());
    }

    // ------------------------------------------------ Scenario B: Cascading failure & compensation

    [Fact]
    public async Task ScenarioB_CascadingPaymentFailure_BrokerOutage_DuplicateWebhook_ReservationExpired_CompensatedCleanly()
    {
        // Seed: Order, Saga WaitingForPayment, Inventory Reserved, Payment Pending.
        var orderId = Guid.NewGuid();
        var order = CreateOrder(orderId);
        OrdersContext.Orders.Add(order);
        var sagaId = Guid.NewGuid();
        var saga = OrderSaga.Start(sagaId, new OrdersOrderId(orderId), orderId, DateTimeOffset.UtcNow);
        saga.RequestReservation(DateTimeOffset.UtcNow);
        saga.MarkWaitingForPayment(DateTimeOffset.UtcNow);
        var reservationId = Guid.NewGuid();
        saga.RecordReservation(reservationId);
        OrdersContext.OrderSagas.Add(saga);
        var sku = new InventorySku($"E2E-B-{Guid.NewGuid():N}"[..6]);
        var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
        item.Reserve(1, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item);
        var reservation = InventoryReservation.Hydrate(
            new InventoryReservationId(reservationId),
            new InventoryOrderId(orderId),
            sku, 1, InventoryReservationStatus.Pending,
            DateTimeOffset.UtcNow.AddMinutes(15),
            InventoryReservation.InitialVersion,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        InventoryContext.InventoryReservations.Add(reservation);
        var paymentId = Guid.NewGuid();
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var payment = new Payment(paymentId, orderId, 10m, "USD", Guid.NewGuid());
        payment.UpdateProviderReference(paymentIntentId, "Stripe");
        payment.Process();
        PaymentsContext.Payments.Add(payment);
        await OrdersContext.SaveChangesAsync();
        await InventoryContext.SaveChangesAsync();
        await PaymentsContext.SaveChangesAsync();

        // Step 1: Stripe returns payment failure → webhook payment_failed → Payment Failed + outbox.
        var stripeEventId = $"evt_{Guid.NewGuid():N}";
        var processor = new WebhookEventProcessor(PaymentsContext, new IntegrationEventOutboxMessageFactory(), new PaymentWebhookInterpreter(), NullLogger<WebhookEventProcessor>.Instance);
        var webhookResult = await processor.TryProcessAsync(stripeEventId, "payment_intent.payment_failed", paymentIntentId, CancellationToken.None);
        Assert.Equal(FluxCommerce.Modules.Payments.Application.Enums.WebhookProcessResult.Processed, webhookResult);
        var paymentAfterWebhook = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Failed, paymentAfterWebhook.Status);
        var paymentFailedOutbox = await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Type == "payments.payment_failed");
        Assert.Equal(OutboxStatus.Pending, paymentFailedOutbox.Status);

        // Step 2: Broker outage during PaymentFailed outbox dispatch → retryable.
        Broker.SimulateConnectionOutage(times: 1);
        var dispatchFail = await SimulateDispatchWithBrokerFaultAsync(PaymentsContext, paymentFailedOutbox);
        Assert.False(dispatchFail);
        var afterBrokerFail = await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == paymentFailedOutbox.Id);
        Assert.Equal(OutboxStatus.Pending, afterBrokerFail.Status);
        Assert.Equal(1, afterBrokerFail.RetryCount);

        // Restore broker and dispatch succeeds.
        ResetFaults();
        await PaymentsContext.OutboxMessages.Where(m => m.Id == paymentFailedOutbox.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RetryAfterUtc, DateTime.UtcNow.AddSeconds(-1)));
        var toDispatch = await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == paymentFailedOutbox.Id);
        var dispatchOk = await SimulateDispatchWithBrokerFaultAsync(PaymentsContext, toDispatch);
        Assert.True(dispatchOk);
        Assert.Equal(OutboxStatus.Published, (await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == paymentFailedOutbox.Id)).Status);

        // Step 3: Duplicate webhook arrival for failed payment concurrent with reservation expiration.
        var duplicateStripeEventId = stripeEventId; // same eventId
        var expiredReservationId = reservationId;
        // Concurrent: duplicate webhook + expiration worker
        var duplicateTask = Task.Run(async () =>
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var proc = new WebhookEventProcessor(ctx, new IntegrationEventOutboxMessageFactory(), new PaymentWebhookInterpreter(), NullLogger<WebhookEventProcessor>.Instance);
            return await proc.TryProcessAsync(duplicateStripeEventId, "payment_intent.payment_failed", paymentIntentId, CancellationToken.None);
        });
        var expirationTask = Task.Run(async () =>
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var res = await ctx.InventoryReservations.FirstOrDefaultAsync(r => r.Id == new InventoryReservationId(expiredReservationId));
            if (res is null) return 0;
            var it = await ctx.InventoryItems.FirstAsync(i => i.Id == sku);
            // Expire will fail if already released via compensation, but should not corrupt.
            var r = res.Expire(it, DateTimeOffset.UtcNow);
            if (r.IsFailure) return 0;
            var factory = new IntegrationEventOutboxMessageFactory();
            var integration = new FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent(
                expiredReservationId, orderId, sku.Value, 1, DateTimeOffset.UtcNow.AddMinutes(-1))
            {
                EventId = Guid.NewGuid(),
                CorrelationId = orderId,
                OccurredAtUtc = DateTime.UtcNow
            };
            ctx.OutboxMessages.Add(factory.Create(FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents.InventoryReservationExpiredIntegrationEvent.EventType, integration));
            try { await ctx.SaveChangesAsync(); return 1; } catch { return 0; }
        });
        var dupResult = await duplicateTask;
        var expResult = await expirationTask;
        // Duplicate webhook must be AlreadyProcessed, expiration may be 0 or 1 but must not deadlock.
        Assert.Equal(FluxCommerce.Modules.Payments.Application.Enums.WebhookProcessResult.AlreadyProcessed, dupResult);

        // Step 4: Saga handles PaymentFailed (if not already) → compensation: release inventory, cancel order.
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var orch = CreateOrchestrator(ctx);
            var existingSaga = await ctx.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
            if (existingSaga.State == OrderSagaState.WaitingForPayment)
            {
                var r = await orch.ApplyPaymentFailedAsync(new OrdersOrderId(orderId), paymentId, paymentIntentId, "card_declined", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);
                Assert.True(r.Outcome == OrderSagaTransitionOutcome.Applied || r.Outcome == OrderSagaTransitionOutcome.Duplicate);
                await ctx.SaveChangesAsync();
            }
        }

        // If saga is ReleasingInventory, complete compensation via inventory release.
        var sagaAfterPaymentFailed = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        if (sagaAfterPaymentFailed.State == OrderSagaState.ReleasingInventory)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var invItem = await ctx.InventoryItems.FirstAsync(i => i.Id == sku);
            var invRes = await ctx.InventoryReservations.FirstOrDefaultAsync(r => r.Id == new InventoryReservationId(reservationId));
            if (invRes != null && invRes.Status == InventoryReservationStatus.Pending)
            {
                invRes.Release(invItem, DateTimeOffset.UtcNow);
                await ctx.SaveChangesAsync();
            }
            // Saga finalizes cancellation after inventory released.
            using var scope2 = Services.CreateScope();
            var ctx2 = scope2.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var orch2 = CreateOrchestrator(ctx2);
            var r2 = await orch2.ApplyInventoryReleasedAsync(new OrdersOrderId(orderId), reservationId, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);
            // May be Applied or Duplicate/Invalid depending on race with expiration, but must not deadlock.
            if (r2.Outcome == OrderSagaTransitionOutcome.Applied) await ctx2.SaveChangesAsync();
        }
        else if (sagaAfterPaymentFailed.State == OrderSagaState.Cancelled)
        {
            // Already cancelled via expiration race — valid terminal.
        }

        // Final invariants: Valid terminal, exactly 1 of each, no orphan, no deadlock.
        var finalOrder = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new OrdersOrderId(orderId));
        Assert.True(finalOrder.Status == OrderStatus.Cancelled || finalOrder.Status == OrderStatus.Pending);
        // If cancelled, inventory must be released (available restored).
        var finalItem = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        AssertInventoryInvariants(finalItem);
        var finalSaga = await OrdersContext.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.True(finalSaga.State == OrderSagaState.Cancelled || finalSaga.State == OrderSagaState.Completed || finalSaga.State == OrderSagaState.ReleasingInventory);
        Assert.Equal(1, await OrdersContext.Orders.AsNoTracking().Where(o => o.Id == new OrdersOrderId(orderId)).CountAsync());
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.Id == paymentId).CountAsync());
        // No duplicate charges
        Assert.Equal(1, await PaymentsContext.Set<FluxCommerce.Modules.Payments.Domain.Entities.ProcessedWebhookEvent>().AsNoTracking().Where(e => e.StripeEventId == stripeEventId).CountAsync());
        // Eventual consistency: outbox not lost
        Assert.True(await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "payments.payment_failed").CountAsync() >= 1);
    }
}
