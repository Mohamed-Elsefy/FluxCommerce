using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

public class OrderSagaPaymentFailedTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset FailedAt = new(2026, 8, 26, 14, 0, 0, TimeSpan.Zero);

    private async Task<(FluxCommerce.Modules.Orders.Domain.Aggregates.Order order, OrderSaga saga)> CreateWaitingForPaymentAsync(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext ctx)
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 1), ("SKU-2", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservation1 = Guid.NewGuid();
        var reservation2 = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservation1, Guid.NewGuid(), Guid.NewGuid(), FailedAt);
        await ctx.SaveChangesAsync();
        // second reservation is duplicate path (WaitingForPayment)
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservation2, Guid.NewGuid(), Guid.NewGuid(), FailedAt);
        await ctx.SaveChangesAsync();
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Equal(2, saga.ReservationIds.Count);
        return (order, saga);
    }

    [Fact]
    public async Task ApplyPaymentFailed_HappyPath_TransitionsToReleasingInventory_AndStagesRelease()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        var paymentId = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        var causation = Guid.NewGuid();
        var result = await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test_123", "card_declined", correlation, causation, FailedAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        Assert.Equal("card_declined", saga.PaymentFailureReason);

        await using var fresh = CreateContext();
        var releases = await fresh.OutboxMessages.Where(m => m.Type == InventoryReleaseRequestedIntegrationEvent.EventType).ToListAsync();
        Assert.Equal(2, releases.Count);
        foreach (var msg in releases)
        {
            Assert.Equal(correlation, msg.CorrelationId);
            Assert.Equal(causation, msg.CausationId);
        }
        // Idempotent second check: order still pending (cancellation is 10.7)
        Assert.Equal(FluxCommerce.Modules.Orders.Domain.Enums.OrderStatus.Pending, (await fresh.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApplyPaymentFailed_DuplicateOnReleasingInventory_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        var paymentId = Guid.NewGuid();
        await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), FailedAt);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;
        var countAfterFirst = await ctx.OutboxMessages.CountAsync(m => m.Type == InventoryReleaseRequestedIntegrationEvent.EventType);

        var duplicate = await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), FailedAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        Assert.Equal(versionAfterFirst, saga.Version);
        Assert.Equal(countAfterFirst, await ctx.OutboxMessages.CountAsync(m => m.Type == InventoryReleaseRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task ApplyPaymentFailed_DuplicateOnCancelled_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), FailedAt);
        await ctx.SaveChangesAsync();
        // advance to Cancelled via MarkInventoryReleased
        var saga = await ctx.OrderSagas.SingleAsync();
        saga.MarkInventoryReleased(FailedAt.AddMinutes(1));
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Cancelled, (await ctx.OrderSagas.SingleAsync()).State);

        var duplicate = await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), FailedAt);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.Failed)]
    [InlineData(OrderSagaState.Cancelled)]
    [InlineData(OrderSagaState.Completed)]
    public async Task ApplyPaymentFailed_OnInvalidState_IsRejected(OrderSagaState state)
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        // create saga directly in target state
        var saga = OrderSaga.Hydrate(Guid.NewGuid(), order.Id, Guid.NewGuid(), state, OrderSaga.InitialVersion, PlacedAtUtc, PlacedAtUtc);
        ctx.OrderSagas.Add(saga);
        await ctx.SaveChangesAsync();

        var orchestrator = CreateOrchestrator(ctx);
        var result = await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), FailedAt);

        if (state == OrderSagaState.Cancelled)
            Assert.Equal(OrderSagaTransitionOutcome.Duplicate, result.Outcome);
        else if (state == OrderSagaState.Completed || state == OrderSagaState.Failed)
            Assert.Equal(OrderSagaTransitionOutcome.Conflicting, result.Outcome);
        else
            Assert.Equal(OrderSagaTransitionOutcome.Invalid, result.Outcome);

        Assert.Equal(state, (await ctx.OrderSagas.SingleAsync()).State);
    }

    [Fact]
    public async Task ApplyPaymentFailed_WhenSagaMissing_Throws()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "declined", Guid.NewGuid(), Guid.NewGuid(), FailedAt));
    }

    [Fact]
    public async Task ApplyPaymentFailed_OutOfOrder_AfterCompleted_IsConflicting()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), FailedAt);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);

        var result = await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "late_fail", Guid.NewGuid(), Guid.NewGuid(), FailedAt);
        Assert.Equal(OrderSagaTransitionOutcome.Conflicting, result.Outcome);
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
    }
}
