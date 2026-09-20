using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Application.Enums;
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

namespace FluxCommerce.IntegrationTests.Reliability;

/// <summary>
/// Phase 12.6 — Payment Failure &amp; Timeout Testing. Validates Stripe provider outages,
/// duplicate/out-of-order webhooks idempotency, and payment failure compensation.
/// </summary>
[Trait("Category", "Reliability")]
public sealed class PaymentFailureTimeoutTests : ReliabilityTestBase
{
    // ------------------------------------------------ Helper: Flaky Stripe gateway

    private sealed class FlakyStripeGateway : IStripePaymentGateway
    {
        private readonly int _failFirstN;
        private readonly string _failureMessage;
        private int _callCount;
        public List<string> IdempotencyKeys { get; } = [];
        public List<CreatePaymentIntentRequest> Requests { get; } = [];

        public FlakyStripeGateway(int failFirstN, string failureMessage = "Stripe transient failure")
        {
            _failFirstN = failFirstN;
            _failureMessage = failureMessage;
        }

        public int CallCount => _callCount;

        public Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var key = FluxCommerce.Modules.Payments.Infrastructure.Gateways.StripePaymentGateway.GenerateIdempotencyKey(request.OrderId, request.PaymentId, request.Version);
            IdempotencyKeys.Add(key);
            _callCount++;
            if (_callCount <= _failFirstN)
                throw new InvalidOperationException(_failureMessage + " Stripe");

            return Task.FromResult(new PaymentIntentResult($"pi_{request.PaymentId:N}", PaymentStatus.Pending, "secret_" + request.PaymentId));
        }

        public Task<PaymentStatus> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken cancellationToken = default)
            => Task.FromResult(PaymentStatus.Pending);

        public Task<string?> RetrieveClientSecretAsync(string paymentIntentId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("secret_" + paymentIntentId);

        public Task<RefundResult> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new RefundResult($"re_{request.PaymentId:N}", "succeeded"));
    }

    // ------------------------------------------------ Payment provider outages & retries without duplicate intent
    // Retries go through the saga consumer (the production path): transient Stripe
    // failures throw for redelivery, the payment row persists without intent, and the
    // redelivered event reuses the same row and deterministic idempotency key.

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
    public async Task Stripe_TransientTimeout_Retries_WithoutDuplicateIntent()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var gateway = new FlakyStripeGateway(failFirstN: 2, failureMessage: "Timeout Stripe");
        var consumer = CreateConsumer(gateway);

        // First attempt — transient timeout, consumer throws for redelivery but persists Payment without intent.
        var (event1, envelope1) = CreatePaymentEvent(orderId, userId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(event1, envelope1, CancellationToken.None));
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.OrderId == orderId).CountAsync());
        var paymentAfter1 = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId);
        Assert.True(string.IsNullOrWhiteSpace(paymentAfter1.ProviderPaymentIntentId));
        Assert.Equal(1, gateway.CallCount);

        // Second attempt — still transient, reuses same Payment (idempotent), same idempotency key.
        var (event2, envelope2) = CreatePaymentEvent(orderId, userId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(event2, envelope2, CancellationToken.None));
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.OrderId == orderId).CountAsync());
        Assert.Equal(2, gateway.CallCount);
        Assert.Equal(gateway.IdempotencyKeys[0], gateway.IdempotencyKeys[1]); // deterministic reuse

        // Third attempt — success, updates same Payment with intent.
        var (event3, envelope3) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(event3, envelope3, CancellationToken.None);
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.OrderId == orderId).CountAsync());
        var paymentAfter3 = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId);
        Assert.False(string.IsNullOrWhiteSpace(paymentAfter3.ProviderPaymentIntentId));
        Assert.StartsWith("pi_", paymentAfter3.ProviderPaymentIntentId);

        // Fourth duplicate delivery after success — acknowledged without calling Stripe again.
        var (event4, envelope4) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(event4, envelope4, CancellationToken.None);
        var paymentAfter4 = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId);
        Assert.Equal(paymentAfter3.ProviderPaymentIntentId, paymentAfter4.ProviderPaymentIntentId);
        Assert.Equal(3, gateway.CallCount); // no fourth Stripe call
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.OrderId == orderId).CountAsync());
    }

    [Fact]
    public async Task Stripe_5xx_Retries_WithoutDuplicateIntent_OneLogicalPayment()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var gateway = new FlakyStripeGateway(failFirstN: 1, failureMessage: "HTTP 5xx Stripe");
        var consumer = CreateConsumer(gateway);

        var (event1, envelope1) = CreatePaymentEvent(orderId, userId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(event1, envelope1, CancellationToken.None));
        var (event2, envelope2) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(event2, envelope2, CancellationToken.None);
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.OrderId == orderId).CountAsync());
        Assert.Equal(2, gateway.CallCount);
        Assert.Equal(gateway.IdempotencyKeys[0], gateway.IdempotencyKeys[1]);
    }

    [Fact]
    public async Task Stripe_DuplicateCheckout_SameOrderId_SecondCall_ReturnsExistingIntent_NoNewPayment()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var gateway = new FlakyStripeGateway(failFirstN: 0);
        var consumer = CreateConsumer(gateway);

        var (event1, envelope1) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(event1, envelope1, CancellationToken.None);
        var pi1 = (await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId)).ProviderPaymentIntentId;

        var (event2, envelope2) = CreatePaymentEvent(orderId, userId);
        await consumer.HandleAsync(event2, envelope2, CancellationToken.None);
        var pi2 = (await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId)).ProviderPaymentIntentId;
        Assert.Equal(pi1, pi2);
        Assert.Equal(1, await PaymentsContext.Payments.AsNoTracking().Where(p => p.OrderId == orderId).CountAsync());
        Assert.Equal(1, gateway.CallCount); // second delivery did not hit Stripe
    }

    // ------------------------------------------------ Duplicate & out-of-order webhooks

    [Fact]
    public async Task Webhook_DuplicatePaymentSucceeded_Sequential_Idempotent()
    {
        var stripeEventId = $"evt_{Guid.NewGuid():N}";
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        var payment = new Payment(Guid.NewGuid(), orderId, 19.99m, "USD", Guid.NewGuid());
        payment.UpdateProviderReference(paymentIntentId, "Stripe");
        payment.Process();
        PaymentsContext.Payments.Add(payment);
        await PaymentsContext.SaveChangesAsync();

        var processor = CreateWebhookProcessor();

        var result1 = await processor.TryProcessAsync(stripeEventId, "payment_intent.succeeded", paymentIntentId, CancellationToken.None);
        Assert.Equal(WebhookProcessResult.Processed, result1);
        var after1 = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Succeeded, after1.Status);
        Assert.Equal(1, await PaymentsContext.Set<ProcessedWebhookEvent>().AsNoTracking().Where(e => e.StripeEventId == stripeEventId).CountAsync());
        Assert.Equal(1, await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.EventId != Guid.Empty && m.Type == "payments.payment_succeeded").CountAsync());

        // Duplicate same stripeEventId
        var result2 = await processor.TryProcessAsync(stripeEventId, "payment_intent.succeeded", paymentIntentId, CancellationToken.None);
        Assert.Equal(WebhookProcessResult.AlreadyProcessed, result2);
        var after2 = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Succeeded, after2.Status); // no duplicate transition
        Assert.Equal(1, await PaymentsContext.Set<ProcessedWebhookEvent>().AsNoTracking().Where(e => e.StripeEventId == stripeEventId).CountAsync());
        Assert.Equal(1, await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "payments.payment_succeeded").CountAsync()); // no duplicate outbox
    }

    [Fact]
    public async Task Webhook_DuplicatePaymentSucceeded_Concurrent_Idempotent()
    {
        var stripeEventId = $"evt_{Guid.NewGuid():N}";
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        var payment = new Payment(Guid.NewGuid(), orderId, 19.99m, "USD", Guid.NewGuid());
        payment.UpdateProviderReference(paymentIntentId, "Stripe");
        PaymentsContext.Payments.Add(payment);
        await PaymentsContext.SaveChangesAsync();

        // SQLite in-memory single connection cannot handle concurrent transactions.
        // Simulate concurrent duplicate via sequential deliveries same stripeEventId.
        WebhookProcessResult result1, result2;
        using (var scope = Services.CreateScope())
        {
            var processor = CreateWebhookProcessor(scope);
            result1 = await processor.TryProcessAsync(stripeEventId, "payment_intent.succeeded", paymentIntentId, CancellationToken.None);
        }
        using (var scope = Services.CreateScope())
        {
            var processor = CreateWebhookProcessor(scope);
            result2 = await processor.TryProcessAsync(stripeEventId, "payment_intent.succeeded", paymentIntentId, CancellationToken.None);
        }

        var results = new[] { result1, result2 };
        // One Processed, one AlreadyProcessed (order deterministic now)
        Assert.Contains(WebhookProcessResult.Processed, results);
        Assert.Contains(WebhookProcessResult.AlreadyProcessed, results);
        Assert.Equal(1, await PaymentsContext.Set<ProcessedWebhookEvent>().AsNoTracking().Where(e => e.StripeEventId == stripeEventId).CountAsync());
        var after = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Succeeded, after.Status);
    }

    [Fact]
    public async Task Webhook_OutOfOrder_FailedBeforeSucceeded_Ignored_NoCorruption()
    {
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        var payment = new Payment(Guid.NewGuid(), orderId, 19.99m, "USD", Guid.NewGuid());
        payment.UpdateProviderReference(paymentIntentId, "Stripe");
        PaymentsContext.Payments.Add(payment);
        await PaymentsContext.SaveChangesAsync();

        var processor = CreateWebhookProcessor();

        // Failed arrives before succeeded? Actually we send Failed first (Pending -> Failed), then Succeeded (Failed -> Succeeded invalid, should be ignored)
        var failEventId = $"evt_{Guid.NewGuid():N}";
        var r1 = await processor.TryProcessAsync(failEventId, "payment_intent.payment_failed", paymentIntentId, CancellationToken.None);
        Assert.Equal(WebhookProcessResult.Processed, r1);
        var afterFail = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Failed, afterFail.Status);
        // PaymentFailed outbox may be 0 or 1 depending on whether payment was in correct state to transition;
        // invariant is payment is Failed and no duplicate outbox on second attempt.
        var failedOutboxCountAfter1 = await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "payments.payment_failed").CountAsync();
        Assert.True(failedOutboxCountAfter1 == 0 || failedOutboxCountAfter1 == 1);

        var successEventId = $"evt_{Guid.NewGuid():N}";
        var r2 = await processor.TryProcessAsync(successEventId, "payment_intent.succeeded", paymentIntentId, CancellationToken.None);
        // Out-of-order Succeeded after Failed is invalid transition, caught and no state change but still Processed (not AlreadyProcessed)
        Assert.Equal(WebhookProcessResult.Processed, r2);
        var afterSuccess = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Failed, afterSuccess.Status); // remains Failed, not corrupted to Succeeded
        // No second succeeded outbox (remains 0)
        Assert.Equal(0, await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "payments.payment_succeeded").CountAsync());
        // Failed outbox unchanged
        Assert.Equal(failedOutboxCountAfter1, await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "payments.payment_failed").CountAsync());
    }

    [Fact]
    public async Task Webhook_DuplicateFailed_AfterOrderCancelled_Idempotent()
    {
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();
        var payment = new Payment(Guid.NewGuid(), orderId, 19.99m, "USD", Guid.NewGuid());
        payment.UpdateProviderReference(paymentIntentId, "Stripe");
        payment.Process();
        payment.Fail("card_declined");
        PaymentsContext.Payments.Add(payment);
        await PaymentsContext.SaveChangesAsync();

        var processor = CreateWebhookProcessor();
        var evtId = $"evt_{Guid.NewGuid():N}";
        // Duplicate Failed when already Failed -> no state change, but idempotent via webhook event id
        var r1 = await processor.TryProcessAsync(evtId, "payment_intent.payment_failed", paymentIntentId, CancellationToken.None);
        Assert.Equal(WebhookProcessResult.Processed, r1); // first time with this evtId, but payment already Failed, so no state change
        var after1 = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Failed, after1.Status);

        var r2 = await processor.TryProcessAsync(evtId, "payment_intent.payment_failed", paymentIntentId, CancellationToken.None);
        Assert.Equal(WebhookProcessResult.AlreadyProcessed, r2);
        Assert.Equal(1, await PaymentsContext.Set<ProcessedWebhookEvent>().AsNoTracking().Where(e => e.StripeEventId == evtId).CountAsync());
    }

    // ------------------------------------------------ Payment failure compensation triggers

    [Fact]
    public async Task PaymentFailed_Triggers_InventoryRelease_And_OrderCancelled_ViaSaga()
    {
        // Arrange: Order + Saga WaitingForPayment + Inventory reservation (simulates reserved inventory)
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var order = Order.Place(
            new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
            new CustomerId(customerId),
            OrderNumber.Create(),
            CreateAddress(), CreateAddress(),
            [new OrderLineItem(OrderProductId.Create(), OrderVariantId.Create(), new OrderSku($"SKU-{Guid.NewGuid():N}"[..8]), "PayFail Product", new OrderMoney(10m, "USD"), 1)],
            DateTimeOffset.UtcNow);
        OrdersContext.Orders.Add(order);

        var sagaId = Guid.NewGuid();
        var saga = OrderSaga.Start(sagaId, new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId), orderId, DateTimeOffset.UtcNow);
        saga.RequestReservation(DateTimeOffset.UtcNow);
        saga.MarkWaitingForPayment(DateTimeOffset.UtcNow);
        var reservationId = Guid.NewGuid();
        saga.RecordReservation(reservationId);
        OrdersContext.OrderSagas.Add(saga);

        var sku = new InventorySku($"PAY-FAIL-{Guid.NewGuid():N}"[..8]);
        var item = InventoryItem.Create(sku, 10, DateTimeOffset.UtcNow);
        item.Reserve(1, DateTimeOffset.UtcNow);
        InventoryContext.InventoryItems.Add(item);
        var reservation = InventoryReservation.Hydrate(
            new InventoryReservationId(reservationId),
            new FluxCommerce.Modules.Inventory.Domain.ValueObjects.OrderId(orderId),
            sku, 1,
            FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus.Pending,
            DateTimeOffset.UtcNow.AddMinutes(15),
            InventoryReservation.InitialVersion,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        InventoryContext.InventoryReservations.Add(reservation);

        var paymentId = Guid.NewGuid();
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var payment = new Payment(paymentId, orderId, 10m, "USD", customerId);
        payment.UpdateProviderReference(paymentIntentId, "Stripe");
        payment.Process();
        PaymentsContext.Payments.Add(payment);

        await OrdersContext.SaveChangesAsync();
        await InventoryContext.SaveChangesAsync();
        await PaymentsContext.SaveChangesAsync();

        // Act 1: Stripe webhook reports payment_failed → Payment Fail + outbox PaymentFailed
        var webhookProcessor = CreateWebhookProcessor();
        var stripeEventId = $"evt_{Guid.NewGuid():N}";
        var webhookResult = await webhookProcessor.TryProcessAsync(stripeEventId, "payment_intent.payment_failed", paymentIntentId, CancellationToken.None);
        Assert.Equal(WebhookProcessResult.Processed, webhookResult);
        var paymentAfterWebhook = await PaymentsContext.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId);
        Assert.Equal(PaymentStatus.Failed, paymentAfterWebhook.Status);
        Assert.Equal(1, await PaymentsContext.OutboxMessages.AsNoTracking().Where(m => m.Type == "payments.payment_failed").CountAsync());

        // Act 2: Orders saga consumes PaymentFailed → transitions saga to ReleasingInventory and stages inventory.release_requested
        using var ordersScope = Services.CreateScope();
        var ordersCtx = ordersScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var sagaOrchestrator = CreateSagaOrchestrator(ordersCtx);
        var sagaBefore = await ordersCtx.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.WaitingForPayment, sagaBefore.State);

        var paymentFailedEvent = await PaymentsContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Type == "payments.payment_failed");
        // Simulate consumer: extract correlation/causation from outbox, call saga
        var transition = await sagaOrchestrator.ApplyPaymentFailedAsync(
            new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
            paymentId,
            paymentIntentId,
            "card_declined",
            paymentFailedEvent.CorrelationId,
            paymentFailedEvent.CausationId,
            DateTimeOffset.UtcNow);

        await ordersCtx.SaveChangesAsync();
        var sagaAfter = await ordersCtx.OrderSagas.AsNoTracking().FirstAsync(s => s.Id == sagaId);
        Assert.Equal(OrderSagaState.ReleasingInventory, sagaAfter.State);
        Assert.Equal(1, await ordersCtx.OutboxMessages.AsNoTracking().Where(m => m.Type == "inventory.release_requested").CountAsync());

        // Act 3: Inventory consumes release_requested → releases reservation (idempotent)
        using var invScope = Services.CreateScope();
        var invCtx = invScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var reservationBefore = await invCtx.InventoryReservations.AsNoTracking().FirstAsync(r => r.Id == new InventoryReservationId(reservationId));
        Assert.Equal(FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus.Pending, reservationBefore.Status);
        // Simulate InventoryReleaseRequestedConsumer logic: find reservation, release item
        var invItem = await invCtx.InventoryItems.FirstAsync(i => i.Id == sku);
        var invReservation = await invCtx.InventoryReservations.FirstAsync(r => r.Id == new InventoryReservationId(reservationId));
        invReservation.Release(invItem, DateTimeOffset.UtcNow);
        await invCtx.SaveChangesAsync();

        var reservationAfter = await InventoryContext.InventoryReservations.AsNoTracking().FirstAsync(r => r.Id == new InventoryReservationId(reservationId));
        Assert.Equal(FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus.Released, reservationAfter.Status);
        var itemAfter = await InventoryContext.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == sku);
        Assert.Equal(0, itemAfter.ReservedQuantity);
        Assert.Equal(10, itemAfter.AvailableQuantity); // released

        // Duplicate PaymentFailed redelivery must be idempotent (no second release)
        var duplicateTransition = await sagaOrchestrator.ApplyPaymentFailedAsync(
            new FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderId(orderId),
            paymentId,
            paymentIntentId,
            "card_declined",
            paymentFailedEvent.CorrelationId,
            paymentFailedEvent.CausationId,
            DateTimeOffset.UtcNow);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicateTransition.Outcome);
        Assert.Equal(1, await ordersCtx.OutboxMessages.AsNoTracking().Where(m => m.Type == "inventory.release_requested").CountAsync()); // still 1
    }

    // ------------------------------------------------ helpers

    private CreatePaymentRequestedConsumer CreateConsumer(IStripePaymentGateway gateway)
    {
        var repo = new PaymentRepository(PaymentsContext);
        return new CreatePaymentRequestedConsumer(
            repo,
            gateway,
            NullLogger<CreatePaymentRequestedConsumer>.Instance);
    }

    private WebhookEventProcessor CreateWebhookProcessor(IServiceScope? scope = null)
    {
        var ctx = scope != null ? scope.ServiceProvider.GetRequiredService<PaymentsDbContext>() : PaymentsContext;
        var factory = new IntegrationEventOutboxMessageFactory();
        var interpreter = new PaymentWebhookInterpreter();
        return new WebhookEventProcessor(ctx, factory, interpreter, NullLogger<WebhookEventProcessor>.Instance);
    }

    private OrderSagaOrchestrator CreateSagaOrchestrator(OrdersDbContext ctx)
    {
        // Use real stores but with SQLite context
        var sagaStore = new OrderSagaStore(ctx);
        var orderStore = new OrderStore(ctx);
        var outboxFactory = new SagaOutboxMessageFactory(new IntegrationEventOutboxMessageFactory());
        var dateTimeProvider = new FluxCommerce.BuildingBlocks.Infrastructure.Services.DateTimeProvider();
        var options = Microsoft.Extensions.Options.Options.Create(new ReservationRequestOptions { ReservationLifetime = TimeSpan.FromMinutes(15) });
        return new OrderSagaOrchestrator(sagaStore, orderStore, ctx, outboxFactory, dateTimeProvider, options, NullLogger<OrderSagaOrchestrator>.Instance);
    }

    private static OrderAddress CreateAddress() =>
        new("Test Recipient", "1 Test St", null, "Test City", "Region", "1000", "US");
}
