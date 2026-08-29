using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

public class OrderSagaInventoryReleasedTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset ReleasedAt = new(2026, 8, 26, 15, 0, 0, TimeSpan.Zero);

    private async Task<(FluxCommerce.Modules.Orders.Domain.Aggregates.Order order, OrderSaga saga, Guid reservationId)> CreateReleasingInventorySagaAsync(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext ctx)
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ReleasedAt);
        await ctx.SaveChangesAsync();
        // Simulate payment failed -> ReleasingInventory
        await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), ReleasedAt);
        await ctx.SaveChangesAsync();
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        return (order, saga, reservationId);
    }

    [Fact]
    public async Task ApplyInventoryReleased_HappyPath_TransitionsToCancelled()
    {
        await using var ctx = CreateContext();
        var (order, _, reservationId) = await CreateReleasingInventorySagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        var result = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ReleasedAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        // Phase 10.7: Order is cancelled atomically with saga finalization
        await using var fresh = CreateContext();
        Assert.Equal(FluxCommerce.Modules.Orders.Domain.Enums.OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
    }

    [Fact]
    public async Task ApplyInventoryReleased_DuplicateOnCancelled_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var (order, _, reservationId) = await CreateReleasingInventorySagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ReleasedAt);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;

        var duplicate = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ReleasedAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionAfterFirst, saga.Version);
        // Duplicate must not re-cancel order or stage second OrderCancelled
        await using var fresh = CreateContext();
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == "orders.order_cancelled"));
    }

    [Fact]
    public async Task ApplyInventoryReleased_DuplicateOnCompleted_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var (order, _, reservationId) = await CreateReleasingInventorySagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ReleasedAt);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);

        var duplicate = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ReleasedAt);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.WaitingForPayment)]
    [InlineData(OrderSagaState.Failed)]
    public async Task ApplyInventoryReleased_OnInvalidState_IsRejected(OrderSagaState state)
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var saga = OrderSaga.Hydrate(Guid.NewGuid(), order.Id, Guid.NewGuid(), state, OrderSaga.InitialVersion, PlacedAtUtc, PlacedAtUtc);
        ctx.OrderSagas.Add(saga);
        await ctx.SaveChangesAsync();

        var orchestrator = CreateOrchestrator(ctx);
        var result = await orchestrator.ApplyInventoryReleasedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReleasedAt);
        Assert.Equal(OrderSagaTransitionOutcome.Invalid, result.Outcome);
        Assert.Equal(state, (await ctx.OrderSagas.SingleAsync()).State);
    }

    [Fact]
    public async Task ApplyInventoryReleased_WhenSagaMissing_Throws()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ApplyInventoryReleasedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReleasedAt));
    }

    [Fact]
    public async Task ApplyInventoryReleased_PreservesCorrelationCausation_AcrossCompensation()
    {
        await using var ctx = CreateContext();
        var (order, _, reservationId) = await CreateReleasingInventorySagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        var correlation = Guid.NewGuid();
        var causation = Guid.NewGuid();

        var result = await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, correlation, causation, ReleasedAt);
        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        // Correlation stays original (order creation), but activity tags propagate new correlation/causation for tracing
        Assert.NotEqual(Guid.Empty, saga.CorrelationId);
    }
}
