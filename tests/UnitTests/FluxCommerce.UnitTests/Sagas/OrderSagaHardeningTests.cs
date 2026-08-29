using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 10.9 hardening: duplicate, concurrent, out-of-order, and crash recovery.
/// </summary>
public class OrderSagaHardeningTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset At = new(2026, 8, 27, 15, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public async Task Duplicate_PaymentSucceeded_ProducesExactlyOneConfirmation_NoVersionBumpNoDuplicateOutbox()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        var paymentId = Guid.NewGuid();
        var correlation = Guid.NewGuid();

        var first = await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test_123", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;
        var outboxCountAfterFirst = await ctx.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed");

        var duplicate = await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test_123", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, first.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionAfterFirst, saga.Version);
        Assert.Equal(outboxCountAfterFirst, await ctx.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed"));
        Assert.Equal(1, outboxCountAfterFirst);
    }

    [Fact]
    public async Task Duplicate_PaymentFailed_ProducesExactlyOneCompensationDecision_NoDuplicateRelease()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        var paymentId = Guid.NewGuid();
        var correlation = Guid.NewGuid();

        var first = await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "card_declined", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;
        var releaseCountAfterFirst = await ctx.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested");

        var duplicate = await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "card_declined", correlation, Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, first.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        Assert.Equal(versionAfterFirst, saga.Version);
        Assert.Equal(releaseCountAfterFirst, await ctx.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
        Assert.Equal(1, releaseCountAfterFirst);
    }

    [Fact]
    public async Task Concurrent_PaymentSucceededVsPaymentFailed_OneWinsOtherIsConflicting_VersionBumpsOnce()
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        // Seed saga to WaitingForPayment in separate context
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

        // Apply sequentially on same initial version (both read WaitingForPayment) to simulate concurrent delivery without true parallel SQLite access
        var resultA = await orchA.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        var resultB = await orchB.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), At);

        Assert.Equal(OrderSagaTransitionOutcome.Applied, resultA.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Applied, resultB.Outcome);
        // One will commit, the other will throw concurrency on SaveChanges
        await ctxA.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());

        await using var fresh = CreateContext();
        var saga = await fresh.OrderSagas.SingleAsync();
        // Winner is either Completed or ReleasingInventory, not both
        Assert.True(saga.State == OrderSagaState.Completed || saga.State == OrderSagaState.ReleasingInventory);
        // Version bumped exactly once from WaitingForPayment (version 3 -> 4)
        Assert.Equal(4, saga.Version);
    }

    [Fact]
    public async Task OutOfOrder_PaymentSucceeded_BeforeInventory_IsInvalid_NoStateCorruption()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        // Saga is WaitingForInventory, not yet WaitingForPayment
        var sagaBefore = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForInventory, sagaBefore.State);
        var versionBefore = sagaBefore.Version;

        var result = await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        Assert.Equal(OrderSagaTransitionOutcome.Invalid, result.Outcome);
        var sagaAfter = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForInventory, sagaAfter.State);
        Assert.Equal(versionBefore, sagaAfter.Version);
    }

    [Fact]
    public async Task CrashRecovery_ReleasingInventory_PendingOutbox_IsRecoverable_AfterRestart()
    {
        // Simulate crash after saga transitioned to ReleasingInventory and staged release, but before outbox dispatched
        // The saga state and outbox message are persisted atomically via SaveChanges, so after "crash" (new context) they are visible
        // If crash happened before SaveChanges, the transaction would have rolled back and redelivery would retry - also safe
        // This test verifies the first case: persisted state is recoverable and duplicate redelivery is idempotent
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        // Simulate restart: new context reads the same DB
        await using var afterCrash = CreateContext();
        var sagaAfterCrash = await afterCrash.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, sagaAfterCrash.State);
        var outboxAfterCrash = await afterCrash.OutboxMessages.Where(m => m.Type == "inventory.release_requested").ToListAsync();
        Assert.Single(outboxAfterCrash);
        Assert.Equal(OutboxStatus.Pending, outboxAfterCrash[0].Status);

        // Simulate redelivery of the same PaymentFailed event after crash: should be Duplicate, no second release
        var orchestratorAfterCrash = CreateOrchestrator(afterCrash);
        var duplicate = await orchestratorAfterCrash.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), At);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(1, await afterCrash.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
    }

    [Fact]
    public async Task CrashRecovery_PendingOutbox_ClaimTimeout_Recovers()
    {
        // Directly test the outbox recovery mechanism used on restart: a message claimed as InProgress but not published
        // should be recovered to Pending after ClaimTimeout
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        // Manually mark the payment request outbox as InProgress with old ProcessedAtUtc to simulate crashed dispatcher
        var outbox = await ctx.OutboxMessages.FirstAsync(m => m.Type == "payments.create_payment_requested");
        await ctx.OutboxMessages.Where(m => m.Id == outbox.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(m => m.Status, OutboxStatus.InProgress)
            .SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow.AddMinutes(-10)));

        await using var afterCrash = CreateContext();
        var store = new FluxCommerce.Modules.Orders.Infrastructure.Outbox.OrdersOutboxStore(afterCrash);
        var recovered = await store.RecoverExpiredClaimsAsync(TimeSpan.FromMinutes(5));
        Assert.Equal(1, recovered);
        var recoveredMsg = await afterCrash.OutboxMessages.FirstAsync(m => m.Id == outbox.Id);
        Assert.Equal(OutboxStatus.Pending, recoveredMsg.Status);
        Assert.Null(recoveredMsg.ProcessedAtUtc);
    }

    [Fact]
    public async Task Duplicate_InventoryReleased_OnReleasingInventory_TransitionsOnce_SecondIsDuplicate()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var reservationId = (await ctx.OrderSagas.SingleAsync()).ReservationIds.First();
        var first = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;

        var duplicate = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), At);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(versionAfterFirst, (await ctx.OrderSagas.SingleAsync()).Version);
    }
}
