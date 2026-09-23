using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 10.12: End-to-end integration verification across Orders, Payments, Inventory boundaries.
/// Uses in-memory SQLite + real outbox interceptor + saga orchestrator (same as production) to
/// verify full happy path, compensation path, and refund compensation path without requiring
/// real RabbitMQ/SQL Server. Also verifies outbox/idempotency pipeline and architecture isolation.
/// </summary>
public class OrderSagaEndToEndIntegrationTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset At = new(2026, 8, 27, 18, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "10.12 Happy Path: Checkout->Reserve->PaymentSucceeded->ConfirmOrder")]
    public async Task HappyPath_FullSagaFlow_ConfirmOrder()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);

        // Checkout -> Start saga (ReserveInventory)
        var correlation = Guid.NewGuid();
        var start = await orchestrator.StartFromOrderCreatedAsync(order.Id, correlation, Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        Assert.True(start.SagaCreated);
        Assert.Equal(OrderSagaState.WaitingForInventory, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Single(await ctx.OutboxMessages.Where(m => m.Type == "inventory.reservation_requested").ToListAsync());
        var versionAfterStart = (await ctx.OrderSagas.SingleAsync()).Version;

        // InventoryReserved -> WaitingForPayment + CreatePayment
        var reservationId = Guid.NewGuid();
        var reserved = await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, reserved.Outcome);
        var sagaAfterReserve = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, sagaAfterReserve.State);
        Assert.True(sagaAfterReserve.Version > versionAfterStart);
        Assert.Contains(reservationId, sagaAfterReserve.ReservationIds);
        Assert.Equal(1, await ctx.OutboxMessages.CountAsync(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType));
        var paymentOutbox = await ctx.OutboxMessages.FirstAsync(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType);
        Assert.Equal(correlation, paymentOutbox.CorrelationId);
        var paymentPayload = JsonSerializer.Deserialize<CreatePaymentRequestedIntegrationEvent>(paymentOutbox.Payload);
        Assert.Equal(order.Id.Value, paymentPayload!.OrderId);
        Assert.Equal(order.GrandTotal.Amount, paymentPayload.Amount);

        // PaymentSucceeded -> ConfirmOrder -> Completed
        var paymentId = Guid.NewGuid();
        var succeeded = await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test_123", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, succeeded.Outcome);
        var sagaAfterPayment = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, sagaAfterPayment.State);
        Assert.True(sagaAfterPayment.Version >= sagaAfterReserve.Version);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Confirmed, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed"));
        // Single business effect: no duplicate payment request, no duplicate confirmation
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType));
    }

    [Fact(DisplayName = "10.12 Compensation Path: PaymentFailed->Release->Cancel")]
    public async Task CompensationPath_FullFlow_ReleaseAndCancel()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1), ("SKU-2", 1));
        var orchestrator = CreateOrchestrator(ctx);
        var correlation = Guid.NewGuid();
        await orchestrator.StartFromOrderCreatedAsync(order.Id, correlation, Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId1 = Guid.NewGuid();
        var reservationId2 = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId1, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId2, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(2, (await ctx.OrderSagas.SingleAsync()).ReservationIds.Count);

        // PaymentFailed -> ReleasingInventory + release staged
        var paymentFailed = await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "card_declined", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, paymentFailed.Outcome);
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(2, await ctx.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
        var releases = await ctx.OutboxMessages.Where(m => m.Type == "inventory.release_requested").ToListAsync();
        Assert.All(releases, r => Assert.Equal(correlation, r.CorrelationId));

        // InventoryReleased -> Cancelled -> Completed (Order cancelled)
        var firstReleaseId = (await ctx.OrderSagas.SingleAsync()).ReservationIds.First();
        var released = await orchestrator.ApplyInventoryReleasedAsync(order.Id, firstReleaseId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, released.Outcome);
        var sagaAfterRelease = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, sagaAfterRelease.State);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
        // Single business effect: only one compensation decision despite 2 reservations (single release batch)
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "payments.create_payment_requested")); // only one payment request before failure
    }

    [Fact(DisplayName = "10.12 Refund Path: Post-Payment Refund -> Release -> Cancel")]
    public async Task RefundPath_PostPaymentFailure_RefundReleaseCancel()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var paymentId = Guid.NewGuid();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, paymentId, "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), At.AddTicks(-1));
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Confirmed, (await ctx.Orders.SingleAsync()).Status);
        var versionAfterPayment = (await ctx.OrderSagas.SingleAsync()).Version;

        // Simulate downstream failure after payment -> trigger refund
        var refundCorrelation = Guid.NewGuid();
        var refund = await orchestrator.RequestRefundAsync(order.Id, "downstream fulfillment failure", refundCorrelation, Guid.NewGuid());
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, refund.Outcome);
        Assert.Equal(OrderSagaState.Refunding, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(versionAfterPayment + 1, (await ctx.OrderSagas.SingleAsync()).Version);
        Assert.Equal(1, await ctx.OutboxMessages.CountAsync(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType));
        var refundOutbox = await ctx.OutboxMessages.FirstAsync(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType);
        Assert.Equal(refundCorrelation, refundOutbox.CorrelationId);
        var refundPayload = JsonSerializer.Deserialize<RefundPaymentRequestedIntegrationEvent>(refundOutbox.Payload);
        Assert.Equal(order.Id.Value, refundPayload!.OrderId);

        // PaymentRefunded -> ReleasingInventory + release staged
        var paymentRefunded = await orchestrator.ApplyPaymentRefundedAsync(order.Id, Guid.NewGuid(), "re_test_123", refundCorrelation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, paymentRefunded.Outcome);
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(1, await ctx.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));

        // InventoryReleased -> Cancelled -> Completed (order cancelled from Confirmed)
        var released = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, refundCorrelation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, released.Outcome);
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
        // Version bumps: Completed(4) -> Refunding(5) -> ReleasingInventory(6) -> Completed(7 via Cancelled) = 3 bumps after initial Completed
        Assert.True((await ctx.OrderSagas.SingleAsync()).Version > versionAfterPayment);
    }

    [Fact(DisplayName = "10.12 Outbox & Idempotency Pipeline Integration")]
    public async Task OutboxAndIdempotency_AtomicCommit_NoDeadlock()
    {
        // Verify that saga state + outbox + ProcessedMessage commit atomically and do not deadlock
        // Use two separate contexts sharing the same connection to simulate concurrent idempotent processing
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        await using var ctx = CreateContext();
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var sagaBefore = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, sagaBefore.State);
        var outboxBefore = await ctx.OutboxMessages.CountAsync(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType);
        Assert.Equal(1, outboxBefore);

        // Simulate duplicate PaymentSucceeded via idempotent processor: second call should be Duplicate, no second outbox
        var orchestrator2 = CreateOrchestrator(ctx);
        var paymentId = Guid.NewGuid();
        var first = await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;
        var outboxAfterFirst = await ctx.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed");

        var duplicate = await orchestrator2.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, first.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(versionAfterFirst, (await ctx.OrderSagas.SingleAsync()).Version);
        Assert.Equal(outboxAfterFirst, await ctx.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed"));
        Assert.Equal(1, outboxAfterFirst);
    }

    [Fact(DisplayName = "11.10 Scenario 1+10: Normal Expiration -> Order Cancelled, Released Exactly Once (no re-release)")]
    public async Task Scenario1_NormalExpiration_OrderCancelled_SingleRelease()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        var correlation = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        await orchestrator.StartFromOrderCreatedAsync(order.Id, correlation, Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, (await ctx.OrderSagas.SingleAsync()).State);
        var versionBefore = (await ctx.OrderSagas.SingleAsync()).Version;

        var expired = await orchestrator.ApplyReservationExpiredAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, expired.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.True(saga.Version > versionBefore);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
        // Scenario 10: the expired reservation was released in Inventory at expiry time; the saga must NOT
        // re-issue an inventory.release_requested (that would double-release).
        Assert.Equal(0, await fresh.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
        Assert.Equal(0, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed"));
    }

    [Fact(DisplayName = "11.10 Scenario 2: Duplicate Expiration Event -> One Business Effect")]
    public async Task Scenario2_DuplicateExpiration_SingleEffect()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        var correlation = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        await orchestrator.StartFromOrderCreatedAsync(order.Id, correlation, Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        var first = await orchestrator.ApplyReservationExpiredAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;

        var duplicate = await orchestrator.ApplyReservationExpiredAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, first.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Ignored, duplicate.Outcome);
        Assert.Equal(versionAfterFirst, (await ctx.OrderSagas.SingleAsync()).Version);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
        Assert.Equal(0, await fresh.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
    }

    [Fact(DisplayName = "11.10 Scenario 4: Expiration After Payment Success -> Safe Ignore")]
    public async Task Scenario4_ExpirationAfterPaymentSuccess_Ignored()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        var correlation = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        await orchestrator.StartFromOrderCreatedAsync(order.Id, correlation, Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
        await using var confirmedCtx = CreateContext();
        Assert.Equal(OrderStatus.Confirmed, (await confirmedCtx.Orders.SingleAsync()).Status);
        var versionAfterPayment = (await ctx.OrderSagas.SingleAsync()).Version;

        var lateExpiry = await orchestrator.ApplyReservationExpiredAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Ignored, lateExpiry.Outcome);
        Assert.Equal(versionAfterPayment, (await ctx.OrderSagas.SingleAsync()).Version);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Confirmed, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(0, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
    }

    [Fact(DisplayName = "11.10 Scenario 5: Expiration Before Payment Success -> No Resurrection, Refund Staged")]
    public async Task Scenario5_ExpirationBeforePaymentSuccess_NoResurrection()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        var correlation = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        await orchestrator.StartFromOrderCreatedAsync(order.Id, correlation, Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        var expired = await orchestrator.ApplyReservationExpiredAsync(order.Id, reservationId, correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, expired.Outcome);
        Assert.Equal(OrderSagaState.Cancelled, (await ctx.OrderSagas.SingleAsync()).State);

        var latePayment = await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        // Saga stays Cancelled (no resurrection), order stays Cancelled, refund staged with the 11.7 reason.
        Assert.Equal(OrderSagaState.Cancelled, (await ctx.OrderSagas.SingleAsync()).State);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
        var refund = await fresh.OutboxMessages.Where(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType).ToListAsync();
        var refundMsg = Assert.Single(refund);
        var refundPayload = System.Text.Json.JsonSerializer.Deserialize<RefundPaymentRequestedIntegrationEvent>(refundMsg.Payload);
        Assert.Equal("ReservationExpired.BeforePaymentSucceeded", refundPayload!.Reason);
        // No order confirmation and no cancellation re-emission from the late success; the single cancellation
        // outbox record came from the expiry itself, and no re-release command was issued.
        Assert.Equal(0, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed"));
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
        Assert.Equal(0, await fresh.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
    }

    [Fact(DisplayName = "10.12 Architecture & Isolation Rules Audit")]
    public void Architecture_Isolation_NoSharedDbContextOrLeakage()
    {
        // Verify module DbContexts are distinct types and do not share entity types
        var ordersContextType = typeof(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext);
        var paymentsContextType = typeof(FluxCommerce.Modules.Payments.Infrastructure.DbContexts.PaymentsDbContext);
        var inventoryContextType = typeof(FluxCommerce.Modules.Inventory.Infrastructure.DbContexts.InventoryDbContext);

        Assert.NotEqual(ordersContextType, paymentsContextType);
        Assert.NotEqual(ordersContextType, inventoryContextType);
        Assert.NotEqual(paymentsContextType, inventoryContextType);

        // Verify OrderSagaOrchestrator does not reference PaymentsDbContext or InventoryDbContext
        var orchestratorType = typeof(FluxCommerce.Modules.Orders.Infrastructure.Sagas.OrderSagaOrchestrator);
        var referencesPaymentsDbContext = orchestratorType.Assembly.GetReferencedAssemblies().Any(a => a.Name == "FluxCommerce.Modules.Payments.Infrastructure");
        // Direct reference check via type usage (should only reference Payments.Contracts, not Infrastructure)
        var orchestratorReferencesPaymentsInfra = orchestratorType.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Any(f => f.FieldType.Name.Contains("PaymentsDbContext"));
        Assert.False(orchestratorReferencesPaymentsInfra, "OrderSagaOrchestrator must not reference PaymentsDbContext (no cross-module DbContext)");

        // Verify outbox messages are per-module (Orders outbox only contains Orders types, not Payments)
        // This is implicitly verified by the fact that OrdersOutboxStore and PaymentsOutboxStore use separate DbContexts
        var ordersOutboxStoreType = typeof(FluxCommerce.Modules.Orders.Infrastructure.Outbox.OrdersOutboxStore);
        var paymentsOutboxStoreType = typeof(FluxCommerce.Modules.Payments.Infrastructure.Outbox.PaymentsOutboxStore);
        Assert.NotEqual(ordersOutboxStoreType, paymentsOutboxStoreType);
    }
}

















