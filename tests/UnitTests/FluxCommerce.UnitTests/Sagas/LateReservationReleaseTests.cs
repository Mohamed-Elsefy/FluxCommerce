using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// B2 coverage: a reservation arriving after compensation started (or after cancellation)
/// proves held stock nobody will release — it must be recorded and released immediately
/// instead of leaking until Inventory expiry.
/// </summary>
public class LateReservationReleaseTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset At = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LateReserved_InReleasingInventory_RecordedAndReleasedImmediately()
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
        await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);

        var lateReservation = Guid.NewGuid();
        var result = await orchestrator.ApplyReservationSuccessAsync(order.Id, lateReservation, Guid.NewGuid(), Guid.NewGuid(), At.AddMinutes(1));
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, result.Outcome);
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Contains(lateReservation, (await ctx.OrderSagas.SingleAsync()).ReservationIds);
        Assert.Single(result.Commands);
        // One release from the payment_failed sweep (first reservation) + one for the latecomer.
        await using var fresh = CreateContext();
        Assert.Equal(2, await fresh.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
    }

    [Fact]
    public async Task LateReserved_InCancelled_RecordedAndReleasedImmediately()
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
        await orchestrator.ApplyReservationExpiredAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Cancelled, (await ctx.OrderSagas.SingleAsync()).State);

        var lateReservation = Guid.NewGuid();
        var result = await orchestrator.ApplyReservationSuccessAsync(order.Id, lateReservation, Guid.NewGuid(), Guid.NewGuid(), At.AddMinutes(1));
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, result.Outcome);
        Assert.Equal(OrderSagaState.Cancelled, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Contains(lateReservation, (await ctx.OrderSagas.SingleAsync()).ReservationIds);
        Assert.Single(result.Commands);
        await using var fresh = CreateContext();
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "inventory.release_requested"));
    }

    [Fact]
    public async Task LateReserved_RedeliveredTwice_ReleasesOnlyOnce()
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
        await orchestrator.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), At);
        await ctx.SaveChangesAsync();

        var lateReservation = Guid.NewGuid();
        var first = await orchestrator.ApplyReservationSuccessAsync(order.Id, lateReservation, Guid.NewGuid(), Guid.NewGuid(), At.AddMinutes(1));
        await ctx.SaveChangesAsync();
        var second = await orchestrator.ApplyReservationSuccessAsync(order.Id, lateReservation, Guid.NewGuid(), Guid.NewGuid(), At.AddMinutes(2));
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, first.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, second.Outcome);
        Assert.Single(first.Commands);
        Assert.Empty(second.Commands);
    }
}
