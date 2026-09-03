using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

public class OrderSagaRefundOrchestratorTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset RefundAt = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private async Task<(FluxCommerce.Modules.Orders.Domain.Aggregates.Order order, FluxCommerce.Modules.Orders.Domain.Sagas.OrderSaga saga)> CreateCompletedSagaAsync(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext ctx)
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(2));
        await ctx.SaveChangesAsync();
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        var reloadedOrder = await ctx.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Confirmed, reloadedOrder.Status);
        return (order, saga);
    }

    [Fact]
    public async Task RequestRefund_FromCompleted_StagesRefundRequest_AndTransitionsToRefunding()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateCompletedSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        var correlation = Guid.NewGuid();
        var result = await orchestrator.RequestRefundAsync(order.Id, "downstream failure", correlation, Guid.NewGuid());
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Refunding, saga.State);

        await using var fresh = CreateContext();
        var outbox = await fresh.OutboxMessages.SingleAsync(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType);
        Assert.Equal(correlation, outbox.CorrelationId);
        var payload = JsonSerializer.Deserialize<RefundPaymentRequestedIntegrationEvent>(outbox.Payload);
        Assert.Equal(order.Id.Value, payload!.OrderId);
        Assert.Equal("downstream failure", payload.Reason);
    }

    [Fact]
    public async Task RequestRefund_DuplicateOnRefunding_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateCompletedSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        await orchestrator.RequestRefundAsync(order.Id, "reason", Guid.NewGuid(), Guid.NewGuid());
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;

        var duplicate = await orchestrator.RequestRefundAsync(order.Id, "reason", Guid.NewGuid(), Guid.NewGuid());
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(versionAfterFirst, (await ctx.OrderSagas.SingleAsync()).Version);
        await using var fresh = CreateContext();
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task ApplyPaymentRefunded_FromRefunding_StagesInventoryRelease()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateCompletedSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.RequestRefundAsync(order.Id, "reason", Guid.NewGuid(), Guid.NewGuid());
        await ctx.SaveChangesAsync();

        var paymentId = Guid.NewGuid();
        var result = await orchestrator.ApplyPaymentRefundedAsync(order.Id, paymentId, "re_test_123", Guid.NewGuid(), Guid.NewGuid(), RefundAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        await using var fresh = CreateContext();
        var releases = await fresh.OutboxMessages.Where(m => m.Type == "inventory.release_requested").ToListAsync();
        Assert.NotEmpty(releases);
    }

    [Fact]
    public async Task ApplyPaymentRefunded_Duplicate_IsIdempotent()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateCompletedSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.RequestRefundAsync(order.Id, "reason", Guid.NewGuid(), Guid.NewGuid());
        await ctx.SaveChangesAsync();
        var paymentId = Guid.NewGuid();
        await orchestrator.ApplyPaymentRefundedAsync(order.Id, paymentId, "re_test", Guid.NewGuid(), Guid.NewGuid(), RefundAt);
        await ctx.SaveChangesAsync();
        var versionAfterFirst = (await ctx.OrderSagas.SingleAsync()).Version;

        var duplicate = await orchestrator.ApplyPaymentRefundedAsync(order.Id, paymentId, "re_test", Guid.NewGuid(), Guid.NewGuid(), RefundAt);
        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(versionAfterFirst, (await ctx.OrderSagas.SingleAsync()).Version);
    }

    [Fact]
    public async Task FullRefundFlow_CompletedToRefundingToReleasingInventoryToCancelledToCompleted()
    {
        await using var ctx = CreateContext();
        var (order, _) = await CreateCompletedSagaAsync(ctx);
        var orchestrator = CreateOrchestrator(ctx);

        await orchestrator.RequestRefundAsync(order.Id, "downstream", Guid.NewGuid(), Guid.NewGuid());
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.Refunding, (await ctx.OrderSagas.SingleAsync()).State);

        await orchestrator.ApplyPaymentRefundedAsync(order.Id, Guid.NewGuid(), "re_test", Guid.NewGuid(), Guid.NewGuid(), RefundAt);
        await ctx.SaveChangesAsync();
        Assert.Equal(OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);

        // Simulate inventory released -> should go to Cancelled then Completed via 10.7 logic
        var reservationId = (await ctx.OrderSagas.SingleAsync()).ReservationIds.First();
        await orchestrator.ApplyInventoryReleasedAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), RefundAt.AddMinutes(1));
        await ctx.SaveChangesAsync();

        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        await using var fresh = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await fresh.Orders.SingleAsync()).Status);
    }
}
