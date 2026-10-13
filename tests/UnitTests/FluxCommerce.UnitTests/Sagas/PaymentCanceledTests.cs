using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// B3 coverage: <c>payments.payment_canceled</c> moves a WaitingForPayment saga to compensation
/// (same release sweep as payment_failed) instead of hanging until reservation expiry.
/// </summary>
public class PaymentCanceledTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset At = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyPaymentCanceled_InWaitingForPayment_ReleasesInventory()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        var paymentId = Guid.NewGuid();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, paymentId, "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        var result = await orchestrator.ApplyPaymentCanceledAsync(order.Id, paymentId, "pi_test", "Stripe reported payment_intent.canceled", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);
        await using var fresh = CreateContext();
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
    }

    [Fact]
    public async Task ApplyPaymentCanceled_DuplicateInReleasingInventory_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        var paymentId = Guid.NewGuid();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, paymentId, "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentCanceledAsync(order.Id, paymentId, "pi_test", "canceled", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        var duplicate = await orchestrator.ApplyPaymentCanceledAsync(order.Id, paymentId, "pi_test", "canceled", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        await using var fresh = CreateContext();
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
    }

    [Fact]
    public async Task ApplyPaymentCanceled_AfterCompleted_ReturnsConflicting()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        var paymentId = Guid.NewGuid();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, paymentId, "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        var result = await orchestrator.ApplyPaymentCanceledAsync(order.Id, paymentId, "pi_test", "canceled", Guid.NewGuid(), Guid.NewGuid(), At);

        Assert.Equal(OrderSagaTransitionOutcome.Conflicting, result.Outcome);
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
    }

    [Fact]
    public async Task ApplyPaymentCanceled_WithMismatchedPaymentId_ReturnsConflicting()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, Guid.NewGuid(), "pi_tracked", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        var result = await orchestrator.ApplyPaymentCanceledAsync(order.Id, Guid.NewGuid(), "pi_stranger", "canceled", Guid.NewGuid(), Guid.NewGuid(), At);

        Assert.Equal(OrderSagaTransitionOutcome.Conflicting, result.Outcome);
        Assert.Equal(OrderSagaState.WaitingForPayment, (await ctx.OrderSagas.SingleAsync()).State);
    }
}
