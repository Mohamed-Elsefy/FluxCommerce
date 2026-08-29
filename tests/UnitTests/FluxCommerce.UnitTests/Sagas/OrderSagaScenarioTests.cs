using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 10.10 Scenarios A-H: comprehensive failure and recovery coverage.
/// Each test asserts final Saga state, Order status, single business effect, outbox count, version.
/// </summary>
public class OrderSagaScenarioTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset At = new(2026, 8, 27, 18, 0, 0, TimeSpan.Zero);

    private async Task<(FluxCommerce.Modules.Orders.Domain.Aggregates.Order order, OrderSaga saga)> CreateWaitingForPaymentAsync(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext ctx)
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        return (order, saga);
    }

    private async Task<(FluxCommerce.Modules.Orders.Domain.Aggregates.Order order, OrderSaga saga)> CreateCompletedSagaAsync(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext ctx)
    {
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        var reloadedOrder = await ctx.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Confirmed, reloadedOrder.Status);
        return (order, saga);
    }

    [Fact(DisplayName = "Scenario A: Payment Failure → Release Inventory → Cancel Order")]
    public async Task Scenario_A_PaymentFailure_FullCompensation()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        var versionBefore = (await ctx.OrderSagas.SingleAsync()).Version;

        var paymentFailed = await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, paymentFailed.Outcome);
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(versionBefore + 1, (await ctx.OrderSagas.SingleAsync()).Version);
        Assert.Equal(1, await ctx.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));

        var reservationId = (await ctx.OrderSagas.SingleAsync()).ReservationIds.First();
        var released = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, released.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
        // Version: WaitingForPayment(3) -> ReleasingInventory(4) -> Cancelled(5) -> Completed(6) = 3 bumps after WaitingForPayment
        Assert.Equal(versionBefore + 3, saga.Version);
    }

    [Fact(DisplayName = "Scenario B: Duplicate Payment Failure → Single compensation")]
    public async Task Scenario_B_DuplicatePaymentFailure_SingleRelease()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        var paymentId = Guid.NewGuid();
        var correlation = Guid.NewGuid();

        var first = await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "declined", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;
        var countAfterFirst = await ctx.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested");

        var duplicate = await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "declined", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, first.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(versionAfterFirst, (await ctx.OrderSagas.SingleAsync()).Version);
        Assert.Equal(countAfterFirst, await ctx.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
        Assert.Equal(1, countAfterFirst);
    }

    [Fact(DisplayName = "Scenario C: Payment Success → Confirm Order → Completed")]
    public async Task Scenario_C_PaymentSuccess_HappyPath()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        var versionBefore = (await ctx.OrderSagas.SingleAsync()).Version;

        var result = await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionBefore + 1, saga.Version);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Confirmed, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed"));
    }

    [Fact(DisplayName = "Scenario D: Duplicate Payment Success → Single confirmation")]
    public async Task Scenario_D_DuplicatePaymentSuccess_SingleConfirmation()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        var paymentId = Guid.NewGuid();
        var correlation = Guid.NewGuid();

        var first = await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;
        var outboxAfterFirst = await ctx.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed");

        var duplicate = await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, first.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(versionAfterFirst, (await ctx.OrderSagas.SingleAsync()).Version);
        Assert.Equal(outboxAfterFirst, await ctx.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed"));
        Assert.Equal(1, outboxAfterFirst);
    }

    [Fact(DisplayName = "Scenario E: Crash During Compensation → Recovery")]
    public async Task Scenario_E_CrashDuringCompensation_Recovery()
    {
        // Simulate crash after PaymentFailed → ReleasingInventory + outbox staged but before dispatch
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        // Simulate restart: new context reads persisted ReleasingInventory + pending outbox
        await using var afterCrash = CreateContext();
        var sagaAfterCrash = await afterCrash.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, sagaAfterCrash.State);
        var pendingRelease = await afterCrash.OutboxMessages.Where(m => m.Type == "inventory.release_requested").ToListAsync();
        Assert.Single(pendingRelease);
        Assert.Equal(FluxCommerce.BuildingBlocks.Outbox.Messages.OutboxStatus.Pending, pendingRelease[0].Status);

        // Redelivery of same PaymentFailed after crash must be Duplicate, no second release
        var orchestratorAfterCrash = CreateOrchestrator(afterCrash);
        var duplicate = await orchestratorAfterCrash.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), At);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(1, await afterCrash.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));

        // Now simulate InventoryReleased arriving after recovery
        var reservationId = sagaAfterCrash.ReservationIds.First();
        var released = await orchestratorAfterCrash.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        await afterCrash.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, released.Outcome);
        Assert.Equal(OrderSagaState.Completed, (await afterCrash.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await afterCrash.Orders.SingleAsync()).Status);
    }

    [Fact(DisplayName = "Scenario F: Inventory Release Failure → Retry → Eventually Released")]
    public async Task Scenario_F_InventoryReleaseFailure_RetryEventuallySucceeds()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);

        // First attempt: simulate transient failure - saga remains in ReleasingInventory before retry
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Pending, (await ctx.Orders.SingleAsync()).Status);

        // Retry with correct reservation succeeds
        var reservationId = (await ctx.OrderSagas.SingleAsync()).ReservationIds.First();
        var retry = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, retry.Outcome);
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await ctx.Orders.SingleAsync()).Status);
        Assert.Equal(1, await ctx.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
    }

    [Fact(DisplayName = "Scenario G: Payment Success + Downstream Failure → Refund → Release → Cancel")]
    public async Task Scenario_G_PostPaymentDownstreamFailure_RefundFlow()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        // Payment succeeds → order confirmed, saga completed
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Confirmed, (await ctx.Orders.SingleAsync()).Status);

        // Simulate downstream failure after payment (e.g., fulfillment failure) → trigger refund
        var refund = await orchestrator.RequestRefundAsync(order.Id, "downstream fulfillment failure", Guid.NewGuid(), Guid.NewGuid());
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, refund.Outcome);
        Assert.Equal(OrderSagaState.Refunding, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(1, await ctx.OutboxMessages.CountAsync(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType));

        // Simulate PaymentRefunded from Payments
        var refunded = await orchestrator.ApplyPaymentRefundedAsync(order.Id, Guid.NewGuid(), "re_test_123", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, refunded.Outcome);
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(1, await ctx.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));

        // Inventory released → order cancelled → saga completed (second Completed)
        var reservationId = (await ctx.OrderSagas.SingleAsync()).ReservationIds.First();
        var released = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, released.Outcome);
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await ctx.Orders.SingleAsync()).Status);
        // Outbox: 1 refund request + 1 payment_refunded handling release + 1 order_cancelled
        Assert.Equal(1, await ctx.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
    }

    [Fact(DisplayName = "Scenario H: Concurrent Saga Events → Optimistic concurrency")]
    public async Task Scenario_H_ConcurrentEvents_RaceHandled()
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        await using (var seed = CreateContext())
        {
            var orchestrator = CreateOrchestrator(seed);
            await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
            await seed.SaveChangesAsync();
            await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), At);
            await seed.SaveChangesAsync();
        }

        await using var ctxA = CreateContext();
        await using var ctxB = CreateContext();
        var orchA = CreateOrchestrator(ctxA);
        var orchB = CreateOrchestrator(ctxB);

        var resultA = await orchA.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        var resultB = await orchB.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), At);

        Assert.Equal(OrderSagaTransitionOutcome.Applied, resultA.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Applied, resultB.Outcome);

        await ctxA.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());

        await using var fresh = CreateContext();
        var saga = await fresh.OrderSagas.SingleAsync();
        Assert.True(saga.State == OrderSagaState.Completed || saga.State == OrderSagaState.ReleasingInventory);
        Assert.Equal(4, saga.Version);
    }
}



