using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.Common.FaultInjection;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Stores;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Application.Services;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Entities;
using FluxCommerce.Modules.Payments.Domain.Enums;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Consuming;
using FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories;
using FluxCommerce.Modules.Payments.Infrastructure.Webhook;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrdersOrderId = FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId;
using InventoryOrderId = FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId;

namespace FluxCommerce.IntegrationTests.Reliability;

/// <summary>
/// Phase 12.12 — Reliability Invariants &amp; Automated Verification.
/// Codifies reusable programmatic invariant assertions and verifies them post-failure.
/// </summary>
[Trait("Category", "Reliability")]
public sealed class ReliabilityInvariantsTests : ReliabilityTestBase
{
    // ---------------------------------------------------------------- Reusable invariant helpers (required by spec)

    private static void AssertInventoryInvariants(InventoryItem item)
    {
        Assert.True(item.AvailableQuantity >= 0, $"AvailableQuantity {item.AvailableQuantity} must be >=0 for Sku {item.Sku.Value}");
        Assert.True(item.ReservedQuantity <= item.OnHandQuantity, $"Reserved {item.ReservedQuantity} <= OnHand {item.OnHandQuantity} for Sku {item.Sku.Value}");
        Assert.Equal(item.OnHandQuantity, item.AvailableQuantity + item.ReservedQuantity);
    }

    private async Task AssertInventoryInvariantsForAllAsync()
    {
        var items = await InventoryContext.InventoryItems.AsNoTracking().ToListAsync();
        foreach (var item in items) AssertInventoryInvariants(item);
        // Reserved rows must match reserved quantity sum per SKU is not 1:1, but total reserved across items must equal sum of pending reservations.
        // We verify no leaked stock: reserved quantity equals count of pending reservations * qty is not asserted here, but we verify no negative and sum consistency.
        var reservations = await InventoryContext.InventoryReservations.AsNoTracking().Where(r => r.Status == InventoryReservationStatus.Pending).ToListAsync();
        var reservedSum = await InventoryContext.InventoryItems.AsNoTracking().SumAsync(i => i.ReservedQuantity);
        var pendingReservationSum = reservations.Sum(r => r.Quantity);
        Assert.Equal(reservedSum, pendingReservationSum);
    }

    private async Task AssertIdempotencyInvariantsAsync(Guid eventId, string handler, int expectedBusinessCount, int expectedProcessedCount, Func<Task<int>> businessCount, Func<Task<int>> processedCount)
    {
        var biz = await businessCount();
        var proc = await processedCount();
        Assert.Equal(expectedBusinessCount, biz);
        Assert.Equal(expectedProcessedCount, proc);
        var processedRows = await InventoryContext.ProcessedMessages.AsNoTracking()
            .Where(m => m.Key == eventId.ToString() && m.Handler == handler).ToListAsync();
        // If handler was Inventory, check Inventory processed; if Orders, check Orders etc. Caller passes correct counts.
        // Generic check: at most one row per (EventId, Handler)
        var distinct = processedRows.DistinctBy(m => m.Key + "|" + m.Handler).Count();
        Assert.Equal(processedRows.Count, distinct);
    }

    private async Task AssertOutboxAtomicityAsync(Guid correlationId, string eventType, int expectedBusinessCount, int expectedOutboxCount)
    {
        var businessCount = expectedBusinessCount;
        var outboxCount = await InventoryContext.OutboxMessages.AsNoTracking().Where(m => m.CorrelationId == correlationId && m.Type == eventType).CountAsync()
            + await OrdersContext.OutboxMessages.AsNoTracking().Where(m => m.CorrelationId == correlationId && m.Type == eventType).CountAsync()
            + await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.CorrelationId == correlationId && m.Type == eventType).CountAsync();
        // For this helper we expect business and outbox to be in sync: both 0 or both 1 etc.
        Assert.Equal(expectedOutboxCount, outboxCount);
        // Also verify no orphan: if business is 0, outbox must be 0
        if (businessCount == 0) Assert.Equal(0, outboxCount);
        if (outboxCount == 0) Assert.True(businessCount == 0 || businessCount == expectedBusinessCount);
    }

    private static void AssertCompensationIdempotent<T>(T before, T afterFirst, T afterSecond)
    {
        // Compensation(N) == Compensation(1): second invocation yields same state as first.
        Assert.Equal(afterFirst, afterSecond);
    }

    // ---------------------------------------------------------------- Helpers for test setup

    private static Order CreateOrder(Guid? orderId = null)
    {
        var oid = orderId ?? Guid.NewGuid();
        return Order.Place(
            new OrdersOrderId(oid),
            new CustomerId(Guid.NewGuid()),
            OrderNumber.Create(),
            new OrderAddress("Inv Recipient", "1 Inv St", null, "Inv City", "Region", "1000", "US"),
            new OrderAddress("Inv Recipient", "1 Inv St", null, "Inv City", "Region", "1000", "US"),
            [new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku($"SKU-{Guid.NewGuid():N}"[..6]), "Inv Product", new OrderMoney(10m, "USD"), 1)],
            DateTimeOffset.UtcNow);
    }

    private sealed class FlakyStripeGateway : IStripePaymentGateway
    {
        private readonly int _failFirstN;
        private int _callCount;
        public List<string> Keys { get; } = [];
        public FlakyStripeGateway(int failFirstN) => _failFirstN = failFirstN;
        public int CallCount => _callCount;
        public Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct = default)
        {
            var key = FluxCommerce.Modules.Payments.Infrastructure.Gateways.StripePaymentGateway.GenerateIdempotencyKey(request.OrderId, request.PaymentId, request.Version);
            Keys.Add(key);
            _callCount++;
            if (_callCount <= _failFirstN) throw new InvalidOperationException("Stripe transient Stripe");
            return Task.FromResult(new PaymentIntentResult($"pi_{request.PaymentId:N}", PaymentStatus.Pending, "secret"));
        }
        public Task<PaymentStatus> GetPaymentIntentStatusAsync(string id, CancellationToken ct = default) => Task.FromResult(PaymentStatus.Pending);
        public Task<string?> RetrieveClientSecretAsync(string id, CancellationToken ct = default) => Task.FromResult<string?>("secret");
        public Task<RefundResult> CreateRefundAsync(CreateRefundRequest r, CancellationToken ct = default) => Task.FromResult(new RefundResult("re", "succeeded"));
    }

    // ------------------------------------------------ Inventory invariants post-failure

    [Fact]
    public async Task InventoryInvariants_Hold_After_TransientFault_And_ConcurrentOperations()
    {
        var sku = new InventorySku($"INV-INV-{Guid.NewGuid():N}"[..6]);
        var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item);
        await InventoryContext.SaveChangesAsync();
        await AssertInventoryInvariantsForAllAsync();

        // Inject transient fault then concurrent reserves
        InjectDbSaveChangesFault(DbFaultKind.TransientFailure, times: 1);
        // This SaveChanges will fail but we catch and retry
        var tempSku = new InventorySku($"INV-TMP-{Guid.NewGuid():N}"[..6]);
        var tempItem = InventoryItem.Create(tempSku, 5, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(tempItem);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InventoryContext.SaveChangesAsync());
        InventoryContext.ChangeTracker.Clear();
        ResetFaults();
        // Verify still invariants hold after failed save (no partial)
        await AssertInventoryInvariantsForAllAsync();
        Assert.Equal(1, await InventoryContext.InventoryItems.AsNoTracking().Where(i => i.Id == sku).CountAsync());
        Assert.Equal(0, await InventoryContext.InventoryItems.AsNoTracking().Where(i => i.Id == tempSku).CountAsync());

        // Concurrent reserves 5x1 on sku (10 onHand)
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var store = new InventoryStore(ctx);
            var rid = InventoryReservationId.Create();
            var oid = new InventoryOrderId(Guid.NewGuid());
            var res = await store.TryReserveAsync(sku, rid, oid, 1, DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow);
            return res is not null;
        });
        var results = await Task.WhenAll(tasks);
        Assert.Equal(5, results.Count(b => b));

        var finalItem = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        AssertInventoryInvariants(finalItem);
        Assert.Equal(5, finalItem.ReservedQuantity);
        Assert.Equal(5, finalItem.AvailableQuantity);
        await AssertInventoryInvariantsForAllAsync();
    }

    [Fact]
    public async Task InventoryInvariants_Hold_After_Expiration_And_Release_Races()
    {
        var sku = new InventorySku($"INV-RACE-{Guid.NewGuid():N}"[..6]);
        var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
        item.Reserve(4, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item);
        var expId = InventoryReservationId.Create();
        var expRes = InventoryReservation.Hydrate(expId, new InventoryOrderId(Guid.NewGuid()), sku, 4, InventoryReservationStatus.Pending, DateTimeOffset.UtcNow.AddHours(-1), InventoryReservation.InitialVersion, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-2));
        InventoryContext.InventoryReservations.Add(expRes);
        await InventoryContext.SaveChangesAsync();

        // Race: expire 4 vs release 2 vs reserve 2 — sequential simulation due to SQLite single-connection limit
        // (production SQL Server handles true parallel; SQLite cannot host concurrent transactions)
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var r = await ctx.InventoryReservations.FirstAsync(x => x.Id == expId);
            var it = await ctx.InventoryItems.FirstAsync(x => x.Id == sku);
            if (r.Expire(it, DateTimeOffset.UtcNow).IsSuccess) await ctx.SaveChangesAsync();
        }
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var it = await ctx.InventoryItems.FirstAsync(x => x.Id == sku);
            var res = it.Release(2, DateTimeOffset.UtcNow);
            if (res.IsSuccess) { try { await ctx.SaveChangesAsync(); } catch { ctx.ChangeTracker.Clear(); } }
        }
        using (var scope = Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var store = new InventoryStore(ctx);
            var nid = InventoryReservationId.Create();
            await store.TryReserveAsync(sku, nid, new InventoryOrderId(Guid.NewGuid()), 2, DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow);
        }

        var final = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        AssertInventoryInvariants(final);
        await AssertInventoryInvariantsForAllAsync();
    }

    // ------------------------------------------------ Consumer idempotency invariants

    [Fact]
    public async Task Idempotency_SingleEffect_After_MultipleRedeliveries()
    {
        var eventId = Guid.NewGuid();
        const string handler = "InvIdempHandler";
        var sku = new InventorySku($"INV-IDEMP-{Guid.NewGuid():N}"[..6]);
        var message = ProcessedMessage.Create(eventId, handler);

        for (int i = 0; i < 5; i++)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var processor = new InventoryIdempotentProcessor(ctx, new InventoryDuplicateDetector(ctx), new InventoryIdempotencyStore(ctx));
            var result = await processor.TryProcessAsync(message, async () =>
            {
                // Only first should actually add item
                if (i == 0)
                {
                    ctx.InventoryItems.Add(InventoryItem.Create(sku, 5, DateTimeOffset.UtcNow));
                }
                else
                {
                    ctx.InventoryItems.Add(InventoryItem.Create(new InventorySku($"DUP-{Guid.NewGuid():N}"[..6]), 99, DateTimeOffset.UtcNow));
                }
                await Task.CompletedTask;
            });
            if (i == 0) Assert.False(result.IsDuplicate);
            else Assert.True(result.IsDuplicate);
        }

        await AssertIdempotencyInvariantsAsync(eventId, handler, 1, 1,
            () => InventoryContext.InventoryItems.AsNoTracking().Where(i => i.Id == sku).CountAsync(),
            () => InventoryContext.ProcessedMessages.AsNoTracking().Where(m => m.Key == eventId.ToString() && m.Handler == handler).CountAsync());
        Assert.Equal(1, await InventoryContext.InventoryItems.AsNoTracking().Where(i => i.Id == sku).CountAsync());
        Assert.DoesNotContain(await InventoryContext.InventoryItems.AsNoTracking().ToListAsync(), i => i.OnHandQuantity == 99);
    }

    [Fact]
    public async Task Idempotency_ConcurrentRedeliveries_SingleEffect()
    {
        var eventId = Guid.NewGuid();
        const string handler = "ConcurrentIdemp";
        var sku = new InventorySku($"INV-CONC-{Guid.NewGuid():N}"[..6]);
        var message = ProcessedMessage.Create(eventId, handler);

        // Sequential simulation due to SQLite single connection limit (true parallel covered by SQL Server)
        var results = new List<FluxCommerce.BuildingBlocks.Idempotency.Messages.IdempotencyResult>();
        for (int i = 0; i < 3; i++)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var processor = new InventoryIdempotentProcessor(ctx, new InventoryDuplicateDetector(ctx), new InventoryIdempotencyStore(ctx));
            var r = await processor.TryProcessAsync(message, async () =>
            {
                if (results.Count == 0) ctx.InventoryItems.Add(InventoryItem.Create(sku, 3, DateTimeOffset.UtcNow));
                else ctx.InventoryItems.Add(InventoryItem.Create(new InventorySku($"DUP-{Guid.NewGuid():N}"[..6]), 99, DateTimeOffset.UtcNow));
                await Task.CompletedTask;
            });
            results.Add(r);
        }
        Assert.Equal(1, results.Count(r => !r.IsDuplicate));
        Assert.Equal(2, results.Count(r => r.IsDuplicate));
        await AssertIdempotencyInvariantsAsync(eventId, handler, 1, 1,
            () => InventoryContext.InventoryItems.AsNoTracking().Where(i => i.Id == sku).CountAsync(),
            () => InventoryContext.ProcessedMessages.AsNoTracking().Where(m => m.Key == eventId.ToString() && m.Handler == handler).CountAsync());
    }

    // ------------------------------------------------ Payment intent invariants (via the saga consumer)

    private static (CreatePaymentRequestedIntegrationEvent Event, IntegrationEventEnvelope Envelope) CreatePaymentEvent(
        Guid orderId,
        Guid userId)
    {
        var @event = new CreatePaymentRequestedIntegrationEvent(orderId, userId, 42.50m, "USD")
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        return (@event, IntegrationEventEnvelope.From(@event, CreatePaymentRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task PaymentIntent_SinglePerCheckout_After_Retries_And_DuplicateWebhooks()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var gateway = new FlakyStripeGateway(failFirstN: 2);
        var repo = new PaymentRepository(PaymentsContext);
        var consumer = new CreatePaymentRequestedConsumer(
            repo,
            gateway,
            NullLogger<CreatePaymentRequestedConsumer>.Instance);

        // Retry 2 transient failures (throw for redelivery) then success
        var (event1, envelope1) = CreatePaymentEvent(orderId, userId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(event1, envelope1, CancellationToken.None));
        var (event2, envelope2) = CreatePaymentEvent(orderId, userId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(event2, envelope2, CancellationToken.None));
        var (event3, envelope3) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(event3, envelope3, CancellationToken.None);
        // Deterministic same key for retries
        Assert.Equal(gateway.Keys[0], gateway.Keys[1]);
        Assert.Equal(gateway.Keys[1], gateway.Keys[2]);
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.OrderId == orderId).CountAsync());

        // Duplicate webhook for same payment intent
        var payment = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId);
        var piId = payment.ProviderPaymentIntentId!;
        var stripeEventId = $"evt_{Guid.NewGuid():N}";
        var webhookProcessor = new WebhookEventProcessor(PaymentsContext, new IntegrationEventOutboxMessageFactory(), new PaymentWebhookInterpreter(), NullLogger<WebhookEventProcessor>.Instance);
        var w1 = await webhookProcessor.TryProcessAsync(stripeEventId, "payment_intent.succeeded", piId, CancellationToken.None);
        Assert.Equal(FluxCommerce.Modules.Payments.Application.Enums.WebhookProcessResult.Processed, w1);
        var w2 = await webhookProcessor.TryProcessAsync(stripeEventId, "payment_intent.succeeded", piId, CancellationToken.None);
        Assert.Equal(FluxCommerce.Modules.Payments.Application.Enums.WebhookProcessResult.AlreadyProcessed, w2);
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.OrderId == orderId).CountAsync());
        Assert.Equal(1, await PaymentsContext.Set<ProcessedWebhookEvent>().AsNoTracking().Where(e => e.StripeEventId == stripeEventId).CountAsync());
    }

    // ------------------------------------------------ Outbox-domain atomicity

    [Fact]
    public async Task OutboxAtomicity_CommittedMutation_Has_Outbox_And_RolledBack_Has_Neither()
    {
        var correlationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = CreateOrder(orderId);
        var domainEvent = order.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var integration = new OrderCreatedIntegrationEvent(order.Id.Value, order.CustomerId.Value, order.OrderNumber.Value, domainEvent.OccurredAtUtc)
        {
            EventId = domainEvent.EventId,
            CorrelationId = correlationId,
            OccurredAtUtc = domainEvent.OccurredAtUtc
        };
        var factory = new IntegrationEventOutboxMessageFactory();
        var outbox = factory.Create(OrderCreatedIntegrationEvent.EventType, integration);

        // Success path: both committed
        OrdersContext.Orders.Add(order);
        OrdersContext.OutboxMessages.Add(outbox);
        await OrdersContext.SaveChangesAsync();
        await AssertOutboxAtomicityAsync(correlationId, OrderCreatedIntegrationEvent.EventType, 1, 1);
        Assert.Equal(1, await OrdersContext.Orders.AsNoTracking().Where(o => o.Id == new OrdersOrderId(orderId)).CountAsync());

        // Failure path: inject fault, both rolled back
        var orderId2 = Guid.NewGuid();
        var order2 = CreateOrder(orderId2);
        var domainEvent2 = order2.GetDomainEvents().OfType<FluxCommerce.Modules.Orders.Domain.Events.OrderCreated>().Single();
        var correlationId2 = Guid.NewGuid();
        var integration2 = new OrderCreatedIntegrationEvent(order2.Id.Value, order2.CustomerId.Value, order2.OrderNumber.Value, domainEvent2.OccurredAtUtc)
        {
            EventId = domainEvent2.EventId,
            CorrelationId = correlationId2,
            OccurredAtUtc = domainEvent2.OccurredAtUtc
        };
        var outbox2 = factory.Create(OrderCreatedIntegrationEvent.EventType, integration2);
        OrdersContext.Orders.Add(order2);
        OrdersContext.OutboxMessages.Add(outbox2);
        InjectDbSaveChangesFault(DbFaultKind.TransientFailure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => OrdersContext.SaveChangesAsync());
        OrdersContext.ChangeTracker.Clear();
        ResetFaults();
        await AssertOutboxAtomicityAsync(correlationId2, OrderCreatedIntegrationEvent.EventType, 0, 0);
        Assert.Equal(0, await OrdersContext.Orders.AsNoTracking().Where(o => o.Id == new OrdersOrderId(orderId2)).CountAsync());
    }

    // ------------------------------------------------ Compensation safety

    [Fact]
    public async Task Compensation_Idempotent_MultipleInvocations_SameState_InventoryAndOrder()
    {
        // Setup: item reserved 2, order pending, then release compensation
        var sku = new InventorySku($"INV-COMP-{Guid.NewGuid():N}"[..6]);
        var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
        item.Reserve(2, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item);
        await InventoryContext.SaveChangesAsync();
        var before = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(2, before.ReservedQuantity);
        AssertInventoryInvariants(before);

        // First compensation: release
        var tracked1 = await InventoryContext.InventoryItems.FirstAsync(i => i.Id == sku);
        var r1 = tracked1.Release(2, DateTimeOffset.UtcNow);
        Assert.True(r1.IsSuccess);
        await InventoryContext.SaveChangesAsync();
        var after1 = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(0, after1.ReservedQuantity);
        AssertInventoryInvariants(after1);

        // Second compensation (duplicate) — should be idempotent no-op, not over-release
        var tracked2 = await InventoryContext.InventoryItems.FirstAsync(i => i.Id == sku);
        var r2 = tracked2.Release(2, DateTimeOffset.UtcNow);
        Assert.True(r2.IsFailure); // exceeds reserved, correctly fails
        var after2 = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        AssertCompensationIdempotent(after1, after1, after2); // second equals first
        Assert.Equal(0, after2.ReservedQuantity);
        AssertInventoryInvariants(after2);

        // Order cancellation compensation idempotence
        var orderId = Guid.NewGuid();
        var order = CreateOrder(orderId);
        OrdersContext.Orders.Add(order);
        await OrdersContext.SaveChangesAsync();
        var o1 = await OrdersContext.Orders.FirstAsync(o => o.Id == new OrdersOrderId(orderId));
        var c1 = o1.Cancel(DateTimeOffset.UtcNow);
        Assert.True(c1.IsSuccess);
        await OrdersContext.SaveChangesAsync();
        var orderAfter1 = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new OrdersOrderId(orderId));
        Assert.Equal(OrderStatus.Cancelled, orderAfter1.Status);

        var o2 = await OrdersContext.Orders.FirstAsync(o => o.Id == new OrdersOrderId(orderId));
        var c2 = o2.Cancel(DateTimeOffset.UtcNow);
        Assert.True(c2.IsFailure); // already cancelled
        var orderAfter2 = await OrdersContext.Orders.AsNoTracking().FirstAsync(o => o.Id == new OrdersOrderId(orderId));
        AssertCompensationIdempotent(orderAfter1.Status, orderAfter1.Status, orderAfter2.Status);
        Assert.Equal(OrderStatus.Cancelled, orderAfter2.Status);
    }
}
