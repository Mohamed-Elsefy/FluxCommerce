using System.Text.Json;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 10.4: PaymentSucceeded → Completed (saga) + Confirmed (order), idempotency, invalid transitions.
/// Phase 11.7: PaymentSucceeded after ReservationExpired (saga Cancelled) → staged refund compensation;
/// PaymentSucceeded before ReservationExpired (saga Completed) → the later expiry is ignored safely.
/// </summary>
public class OrderSagaPaymentSucceededTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset SucceededAt = new(2026, 8, 26, 13, 0, 0, TimeSpan.Zero);

    private async Task<(FluxCommerce.Modules.Orders.Domain.Aggregates.Order order, OrderSaga saga)> CreateWaitingForPaymentSagaAsync(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext ctx)
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        // first reservation success -> WaitingForPayment
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        return (order, saga);
    }

    [Fact]
    public async Task ApplyPaymentSucceeded_HappyPath_CompletesSaga_AndConfirmsOrder()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        var paymentId = Guid.NewGuid();
        var result = await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);

        await using var fresh = CreateContext();
        var reloadedOrder = await fresh.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Confirmed, reloadedOrder.Status);
        // Outbox should contain order_confirmed via interceptor
        var outbox = await fresh.OutboxMessages.Where(m => m.Type == "orders.order_confirmed").ToListAsync();
        Assert.Single(outbox);
    }

    [Fact]
    public async Task ApplyPaymentSucceeded_DuplicateOnCompleted_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        var paymentId = Guid.NewGuid();
        var correlation = Guid.NewGuid();

        await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test", correlation, Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;

        var duplicate = await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test", correlation, Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionAfterFirst, saga.Version);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Confirmed, (await fresh.Orders.SingleAsync()).Status);
        // Should still be single order_confirmed outbox (duplicate does not stage second)
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_confirmed"));
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.Failed)]
    [InlineData(OrderSagaState.ReleasingInventory)]
    public async Task ApplyPaymentSucceeded_OnInvalidState_IsRejected(OrderSagaState state)
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var saga = OrderSaga.Hydrate(Guid.NewGuid(), order.Id, Guid.NewGuid(), state, OrderSaga.InitialVersion, PlacedAtUtc, PlacedAtUtc);
        ctx.OrderSagas.Add(saga);
        await ctx.SaveChangesAsync();

        var orchestrator = CreateOrchestrator(ctx);
        var result = await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        Assert.NotEqual(OrderSagaTransitionOutcome.Applied, result.Outcome);
        Assert.NotEqual(OrderSagaTransitionOutcome.Duplicate, result.Outcome);
        var reloaded = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(state, reloaded.State);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Pending, (await fresh.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApplyPaymentSucceeded_WhenSagaMissing_Throws()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), SucceededAt));
    }

    [Fact]
    public async Task ApplyPaymentSucceeded_OutOfOrder_BeforeInventory_IsInvalid()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        // saga is WaitingForInventory, not WaitingForPayment
        var result = await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        Assert.Equal(OrderSagaTransitionOutcome.Invalid, result.Outcome);
        Assert.Equal(OrderSagaState.WaitingForInventory, (await ctx.OrderSagas.SingleAsync()).State);
    }

    // --------------------------------------------------------------------------------------------
    // Phase 11.7: PaymentSucceeded after ReservationExpired (the reservation-expiry race).
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task ApplyPaymentSucceeded_AfterReservationExpired_StagesRefundWithoutResurrectingSaga()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        // ReservationExpired wins first -> saga Cancelled + order Cancelled (inventory already released).
        var reservationId = Guid.NewGuid();
        var expired = await orchestrator.ApplyReservationExpiredAsync(
            order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, expired.Outcome);
        Assert.Equal(OrderSagaState.Cancelled, (await ctx.OrderSagas.SingleAsync()).State);

        // A duplicate expiry is safely ignored (no re-cancel, no extra outbox).
        var duplicateExpired = await orchestrator.ApplyReservationExpiredAsync(
            order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Ignored, duplicateExpired.Outcome);

        // Now a late PaymentSucceeded arrives. Money was charged but the reservation is gone:
        // a refund must be staged and the terminal saga must NOT be moved back or confirmed.
        var paymentId = Guid.NewGuid();
        var lateSuccess = await orchestrator.ApplyPaymentSucceededAsync(
            order.Id, paymentId, "pi_test_late", Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Conflicting, lateSuccess.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Cancelled, saga.State);

        // Order stays Cancelled (NOT confirmed by the late success) -> no resurrection.
        var orderAfter = await ctx.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, orderAfter.Status);

        // Exactly one refund is staged (no duplicate).
        var refunds = await ctx.OutboxMessages.Where(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType).ToListAsync();
        Assert.Single(refunds);
        var refund = JsonSerializer.Deserialize<RefundPaymentRequestedIntegrationEvent>(refunds[0].Payload);
        Assert.NotNull(refund);
        Assert.Equal(order.Id.Value, refund.OrderId);
        Assert.Equal(paymentId, refund.PaymentId);
        Assert.Equal("ReservationExpired.BeforePaymentSucceeded", refund.Reason);
        Assert.Equal(order.GrandTotal.Amount, refund.Amount);
        Assert.Equal(order.GrandTotal.Currency, refund.Currency);
    }

    [Fact]
    public async Task ApplyPaymentSucceeded_BeforeReservationExpired_ThenExpiryIsIgnored()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateWaitingForPaymentSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        // PaymentSucceeded wins first -> saga Completed + order Confirmed.
        var success = await orchestrator.ApplyPaymentSucceededAsync(
            order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaTransitionOutcome.Applied, success.Outcome);
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);

        // The late ReservationExpired arrives on a Completed saga -> safely ignored, no re-cancel,
        // no extra outbox. The order stays Confirmed (no resurrection either way).
        var lateExpiry = await orchestrator.ApplyReservationExpiredAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SucceededAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Ignored, lateExpiry.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Confirmed, (await fresh.Orders.SingleAsync()).Status);
        Assert.Empty(await fresh.OutboxMessages.Where(m => m.Type == OrderCancelledIntegrationEvent.EventType).ToListAsync());
    }
}
