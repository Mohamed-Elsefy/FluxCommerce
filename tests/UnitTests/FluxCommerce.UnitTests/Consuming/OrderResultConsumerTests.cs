using System.Text.Json;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.UnitTests.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

/// <summary>
/// Phase 8.5/8.6: the Orders result consumers (<c>inventory.reserved</c> / <c>inventory.reservation_failed</c>)
/// are thin adapters that validate the payload and delegate the saga transition to the orchestrator.
/// A structurally valid result event runs through the Orders idempotent-processing boundary and applies
/// the transition (saga Completed on success; saga Failed + Order cancelled on failure); a malformed one
/// throws before the orchestrator is ever reached (the consumer infrastructure retries then dead-letters,
/// per Phase 7.7).
///
/// <para>
/// Phase 11.4 wired <c>inventory.reservation_expired</c> through the consumer (acknowledged exactly-once
/// by a safe orchestrator no-op); sub-phase 11.5 defined the saga expiry state model; sub-phase 11.6
/// completed the orchestrator so a valid expiry from <c>WaitingForPayment</c> cancels both the saga
/// (<c>WaitingForPayment → Cancelled</c>) and the Order (Pending → Cancelled, emitting
/// <c>orders.order_cancelled</c> atomically), while terminal and in-flight-compensation states are safely
/// ignored with no Order mutation and no duplicate outbox. A redelivered expiry is an idempotent no-op.
/// </para>
///
/// <para>
/// Phase 11.7 covers the <c>PaymentSucceeded</c> after <c>ReservationExpired</c> race at the consumer
/// level: once the saga is already <c>Cancelled</c> (expiry won), a late payment success stages an explicit
/// <c>payments.refund_payment_requested</c> compensation (preventing money lock-in) while leaving the
/// terminal saga and the Cancelled order untouched — exactly one refund, no resurrection.
/// </para>
/// </summary>
public class OrderResultConsumerTests : OrderSagaTestBase
{
    [Fact]
    public void InventoryReservedConsumer_HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Orders.ApplyReservationSuccess", InventoryReservedConsumer.StableHandlerName);
    }

    [Fact]
    public void InventoryReservationFailedConsumer_HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Orders.ApplyReservationFailure", InventoryReservationFailedConsumer.StableHandlerName);
    }

    [Fact]
    public async Task Reserved_ValidEvent_AppliesSuccessTransition()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(order.Id.Value);

        await consumer.HandleAsync(@event, EnvelopeFrom(@event));
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaState.WaitingForPayment, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Pending, (await context.Orders.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Reserved_InvalidSku_Throws(string? sku)
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(Guid.NewGuid()) with { Sku = sku ?? string.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Reserved_EmptyReservationId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(Guid.NewGuid()) with { ReservationId = Guid.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Reserved_EmptyOrderId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Reserved_NonPositiveQuantity_Throws(int quantity)
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(Guid.NewGuid()) with { Quantity = quantity };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Failed_ValidEvent_AppliesFailureTransition_AndCancelsOrder()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var consumer = new InventoryReservationFailedConsumer(
            orchestrator,
            NullLogger<InventoryReservationFailedConsumer>.Instance);
        var @event = CreateFailedEvent(order.Id.Value);

        await consumer.HandleAsync(@event, EnvelopeFrom(@event));
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaState.Failed, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Failed_EmptyOrderId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationFailedConsumer(
            orchestrator,
            NullLogger<InventoryReservationFailedConsumer>.Instance);
        var @event = CreateFailedEvent(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Failed_EmptyReason_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationFailedConsumer(
            orchestrator,
            NullLogger<InventoryReservationFailedConsumer>.Instance);
        var @event = CreateFailedEvent(Guid.NewGuid()) with { Reason = string.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Failed_NonPositiveQuantity_Throws(int quantity)
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationFailedConsumer(
            orchestrator,
            NullLogger<InventoryReservationFailedConsumer>.Instance);
        var @event = CreateFailedEvent(Guid.NewGuid()) with { Quantity = quantity };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public void InventoryReservationExpiredConsumer_HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Orders.ApplyReservationExpired", InventoryReservationExpiredConsumer.StableHandlerName);
    }

    [Fact]
    public async Task Expired_ValidEvent_StreamsThrough_DoesNotMutateSagaState()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var consumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        var @event = CreateExpiredEvent(order.Id.Value);

        await consumer.HandleAsync(@event, EnvelopeFrom(@event));
        await context.SaveChangesAsync();

        // Phase 11.4 stub: no saga state change (sub-phases 11.5/11.6 add the expiry transition), but the
        // event still flows through and is safely acknowledged rather than dead-lettered.
        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForInventory, saga.State);
        Assert.Equal(OrderStatus.Pending, (await context.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Expired_ValidEvent_OrchestratorReturnsIgnoredOutcome()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var result = await orchestrator.ApplyReservationExpiredAsync(
            order.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            PlacedAtUtc.AddMinutes(30));

        Assert.Equal(OrderSagaTransitionOutcome.Ignored, result.Outcome);
    }

    [Fact]
    public async Task Expired_EmptyReservationId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        var @event = CreateExpiredEvent(Guid.NewGuid()) with { ReservationId = Guid.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Expired_EmptyOrderId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        var @event = CreateExpiredEvent(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Expired_InvalidSku_Throws(string? sku)
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        var @event = CreateExpiredEvent(Guid.NewGuid()) with { Sku = sku ?? string.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Expired_NonPositiveQuantity_Throws(int quantity)
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        var @event = CreateExpiredEvent(Guid.NewGuid()) with { Quantity = quantity };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Expired_DefaultExpiryTimestamp_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        var @event = CreateExpiredEvent(Guid.NewGuid()) with { ExpiredAtUtc = default };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    // ---- Phase 11.6 reservation-expiry orchestrator ----

    [Fact]
    public async Task Expired_ValidEvent_WaitingForPayment_CancelsSagaAndOrder_AndEmitsOrderCancelled()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        // Reserve success drives WaitingForInventory -> WaitingForPayment (Phase 10).
        var reservedConsumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var reserved = CreateReservedEvent(order.Id.Value);
        await reservedConsumer.HandleAsync(reserved, EnvelopeFrom(reserved));
        await context.SaveChangesAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, (await context.OrderSagas.SingleAsync()).State);

        var expiredConsumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        var expired = CreateExpiredEvent(order.Id.Value);
        await expiredConsumer.HandleAsync(expired, EnvelopeFrom(expired));
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);

        // The Order.Cancel transition raises OrderCancelled -> outbox publishes orders.order_cancelled
        // atomically with the saga state + processed-message record.
        var outbox = await context.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == "orders.order_cancelled")
            .ToListAsync();
        Assert.Single(outbox);
    }

    [Fact]
    public async Task Expired_OnCancelledTerminal_Ignored_NoMutation_NoDuplicateOutbox()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var reservedConsumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        await reservedConsumer.HandleAsync(CreateReservedEvent(order.Id.Value), EnvelopeFrom(CreateReservedEvent(order.Id.Value)));
        await context.SaveChangesAsync();

        var expiredConsumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        await expiredConsumer.HandleAsync(CreateExpiredEvent(order.Id.Value), EnvelopeFrom(CreateExpiredEvent(order.Id.Value)));
        await context.SaveChangesAsync();

        // First expiry cancelled the saga and order.
        Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);
        var outboxCount = await context.OutboxMessages.AsNoTracking().CountAsync();

        // A later (redelivered / late) expiry on the terminal Cancelled state is safely ignored:
        // no saga or order mutation and no duplicate outbox message.
        await expiredConsumer.HandleAsync(CreateExpiredEvent(order.Id.Value), EnvelopeFrom(CreateExpiredEvent(order.Id.Value)));
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);
        Assert.Equal(outboxCount, await context.OutboxMessages.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Expired_Redelivered_ThroughIdempotencyBoundary_DoesNotReExecute()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var reservedConsumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        await reservedConsumer.HandleAsync(CreateReservedEvent(order.Id.Value), EnvelopeFrom(CreateReservedEvent(order.Id.Value)));
        await context.SaveChangesAsync();

        var processor = new OrdersIdempotentProcessor(
            context,
            new OrdersDuplicateDetector(context),
            new OrdersIdempotencyStore(context));
        var expiredConsumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);

        var eventId = Guid.NewGuid();
        var expired = CreateExpiredEvent(order.Id.Value);
        var envelope = IntegrationEventEnvelope.From(expired, InventoryReservationExpiredIntegrationEvent.EventType);

        // First delivery processes the expiry inside the idempotent-processing boundary.
        await processor.TryProcessAsync(
            ProcessedMessage.Create(eventId, InventoryReservationExpiredConsumer.StableHandlerName),
            () => expiredConsumer.HandleAsync(expired, envelope));
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);
        var outboxCount = await context.OutboxMessages.AsNoTracking().CountAsync();

        // Redelivery with the same EventId + HandlerName is acknowledged as an idempotent duplicate
        // and does NOT re-execute the business operation (no state change, no extra outbox message).
        var second = await processor.TryProcessAsync(
            ProcessedMessage.Create(eventId, InventoryReservationExpiredConsumer.StableHandlerName),
            () => expiredConsumer.HandleAsync(expired, envelope));
        await context.SaveChangesAsync();

        Assert.True(second.IsDuplicate);
        Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);
        Assert.Equal(outboxCount, await context.OutboxMessages.AsNoTracking().CountAsync());
    }

    // ---- Phase 11.7 PaymentSucceeded after ReservationExpired (reservation-expiry race) ----

    [Fact]
    public async Task ExpiredThenPaymentSucceeded_StagesSingleRefund_WithoutResurrectingSaga()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var reservedConsumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        await reservedConsumer.HandleAsync(CreateReservedEvent(order.Id.Value), EnvelopeFrom(CreateReservedEvent(order.Id.Value)));
        await context.SaveChangesAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, (await context.OrderSagas.SingleAsync()).State);

        // ReservationExpired wins first -> saga Cancelled + order Cancelled (inventory already released).
        var expiredConsumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        await expiredConsumer.HandleAsync(CreateExpiredEvent(order.Id.Value), EnvelopeFrom(CreateExpiredEvent(order.Id.Value)));
        await context.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);

        // A late PaymentSucceeded (money already charged) must stage a refund and must NOT confirm the
        // order (no resurrection) or move the terminal saga backward.
        var paymentId = Guid.NewGuid();
        var paymentSucceeded = new PaymentSucceededConsumer(
            orchestrator,
            NullLogger<PaymentSucceededConsumer>.Instance);
        var paymentEvent = new PaymentSucceededIntegrationEvent(
            order.Id.Value,
            paymentId,
            "pi_late_123",
            22m,
            "USD",
            "succeeded",
            DateTimeOffset.UtcNow);
        await paymentSucceeded.HandleAsync(
            paymentEvent,
            IntegrationEventEnvelope.From(paymentEvent, PaymentSucceededIntegrationEvent.EventType));
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);

        // Exactly one refund is staged (no duplicate, no extra order cancel).
        var refunds = await context.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType)
            .ToListAsync();
        Assert.Single(refunds);
        var refund = JsonSerializer.Deserialize<RefundPaymentRequestedIntegrationEvent>(refunds[0].Payload);
        Assert.NotNull(refund);
        Assert.Equal(order.Id.Value, refund.OrderId);
        Assert.Equal(paymentId, refund.PaymentId);
        Assert.Equal("ReservationExpired.BeforePaymentSucceeded", refund.Reason);
    }

    // ---- Phase 11.8 duplicate / concurrent delivery hardening for ReservationExpired ----

    [Fact]
    public async Task Expired_TripleDelivery_SingleSagaTransition_SingleBusinessOutcome_SubsequentRedeliveriesAreIdempotentDuplicates()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var reservedConsumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        await reservedConsumer.HandleAsync(CreateReservedEvent(order.Id.Value), EnvelopeFrom(CreateReservedEvent(order.Id.Value)));
        await context.SaveChangesAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, (await context.OrderSagas.SingleAsync()).State);

        var processor = new OrdersIdempotentProcessor(
            context,
            new OrdersDuplicateDetector(context),
            new OrdersIdempotencyStore(context));
        var expiredConsumer = new InventoryReservationExpiredConsumer(
            orchestrator,
            NullLogger<InventoryReservationExpiredConsumer>.Instance);

        // E1 delivered three times (exactly the doc's contract: one valid saga transition, one outcome).
        var eventId = Guid.NewGuid();
        var envelope = IntegrationEventEnvelope.From(CreateExpiredEvent(order.Id.Value), InventoryReservationExpiredIntegrationEvent.EventType);

        var first = await processor.TryProcessAsync(
            ProcessedMessage.Create(eventId, InventoryReservationExpiredConsumer.StableHandlerName),
            () => expiredConsumer.HandleAsync(CreateExpiredEvent(order.Id.Value), envelope));
        await context.SaveChangesAsync();

        // One valid saga transition: WaitingForPayment -> Cancelled, one business outcome: order cancelled.
        Assert.False(first.IsDuplicate);
        Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);
        var cancelledOutboxCount = await context.OutboxMessages.CountAsync(m => m.Type == OrderCancelledIntegrationEvent.EventType);
        Assert.Equal(1, cancelledOutboxCount);

        // Both redeliveries are acknowledged as idempotent duplicates and re-execute nothing.
        for (var i = 0; i < 2; i++)
        {
            var duplicate = await processor.TryProcessAsync(
                ProcessedMessage.Create(eventId, InventoryReservationExpiredConsumer.StableHandlerName),
                () => expiredConsumer.HandleAsync(CreateExpiredEvent(order.Id.Value), envelope));
            await context.SaveChangesAsync();

            Assert.True(duplicate.IsDuplicate);
            Assert.True(duplicate.ProcessedAtUtc.HasValue);
            Assert.Equal(OrderSagaState.Cancelled, (await context.OrderSagas.SingleAsync()).State);
            Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);
            Assert.Equal(cancelledOutboxCount, await context.OutboxMessages.CountAsync(m => m.Type == OrderCancelledIntegrationEvent.EventType));
        }

        // Exactly one ProcessedMessage row for (EventId, HandlerName) -> the composite PK was not duplicated.
        Assert.Equal(1, await context.ProcessedMessages.CountAsync(m => m.Key == eventId.ToString() && m.Handler == InventoryReservationExpiredConsumer.StableHandlerName));
    }

    [Fact]
    public async Task Expired_ConcurrentDelivery_IsolatedConsumers_OneTransition_OneOutcome_LoserRejectedAsDuplicate()
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        // Seed to WaitingForPayment in a dedicated context.
        await using (var seed = CreateContext())
        {
            var orchestrator = CreateOrchestrator(seed);
            await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
            await seed.SaveChangesAsync();
            await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
            await seed.SaveChangesAsync();
            Assert.Equal(OrderSagaState.WaitingForPayment, (await seed.OrderSagas.SingleAsync()).State);
        }

        var eventId = Guid.NewGuid();
        var envelope = IntegrationEventEnvelope.From(CreateExpiredEvent(order.Id.Value), InventoryReservationExpiredIntegrationEvent.EventType);

        // Consumer A and Consumer B are two isolated scopes (own DbContext + own idempotency processor)
        // on the SAME database, racing to apply the SAME ReservationExpired(E1).
        await using var ctxA = CreateContext();
        await using var ctxB = CreateContext();
        var processorA = new OrdersIdempotentProcessor(
            ctxA,
            new OrdersDuplicateDetector(ctxA),
            new OrdersIdempotencyStore(ctxA));
        var processorB = new OrdersIdempotentProcessor(
            ctxB,
            new OrdersDuplicateDetector(ctxB),
            new OrdersIdempotencyStore(ctxB));
        var consumerA = new InventoryReservationExpiredConsumer(
            CreateOrchestrator(ctxA),
            NullLogger<InventoryReservationExpiredConsumer>.Instance);
        var consumerB = new InventoryReservationExpiredConsumer(
            CreateOrchestrator(ctxB),
            NullLogger<InventoryReservationExpiredConsumer>.Instance);

        var resultA = await processorA.TryProcessAsync(
            ProcessedMessage.Create(eventId, InventoryReservationExpiredConsumer.StableHandlerName),
            () => consumerA.HandleAsync(CreateExpiredEvent(order.Id.Value), envelope));
        await ctxA.SaveChangesAsync();

        // Consumer B read the same WaitingForPayment version but the winner committed first: it is
        // detected as an already-processed duplicate and performs no business side effects.
        var resultB = await processorB.TryProcessAsync(
            ProcessedMessage.Create(eventId, InventoryReservationExpiredConsumer.StableHandlerName),
            () => consumerB.HandleAsync(CreateExpiredEvent(order.Id.Value), envelope));
        await ctxB.SaveChangesAsync();

        Assert.False(resultA.IsDuplicate);
        Assert.True(resultB.IsDuplicate);

        await using var fresh = CreateContext();
        // Exactly one saga transition and one business outcome; the order is cancelled, never confirmed.
        Assert.Equal(OrderSagaState.Cancelled, (await fresh.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == OrderCancelledIntegrationEvent.EventType));
        // Exactly one ProcessedMessage -> the loser's insert never committed (composite PK resolved the race).
        Assert.Equal(1, await fresh.ProcessedMessages.CountAsync(m => m.Key == eventId.ToString() && m.Handler == InventoryReservationExpiredConsumer.StableHandlerName));
    }

    private static InventoryReservedIntegrationEvent CreateReservedEvent(Guid orderId) =>
        new(
            Guid.NewGuid(),
            orderId,
            "SKU-RESULT",
            3,
            DateTimeOffset.UtcNow.AddMinutes(30),
            InventoryReservationStatus.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1);

    private static InventoryReservationFailedIntegrationEvent CreateFailedEvent(Guid orderId) =>
        new(orderId, "SKU-RESULT", 3, "Inventory.InsufficientStock");

    private static InventoryReservationExpiredIntegrationEvent CreateExpiredEvent(Guid orderId) =>
        new(
            Guid.NewGuid(),
            orderId,
            "SKU-RESULT",
            3,
            DateTimeOffset.UtcNow.AddMinutes(30));

    private static IntegrationEventEnvelope EnvelopeFrom(IIntegrationEvent @event) =>
        IntegrationEventEnvelope.From(@event, @event is InventoryReservedIntegrationEvent
            ? InventoryReservedIntegrationEvent.EventType
            : @event is InventoryReservationFailedIntegrationEvent
                ? InventoryReservationFailedIntegrationEvent.EventType
                : InventoryReservationExpiredIntegrationEvent.EventType);
}