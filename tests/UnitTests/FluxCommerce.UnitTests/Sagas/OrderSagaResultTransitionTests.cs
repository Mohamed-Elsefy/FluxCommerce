using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 8.6: applying the Inventory reservation OUTCOME through the saga (plan §11). A success advances
/// <c>WaitingForInventory → Completed</c> and leaves the Order Pending; a failure advances
/// <c>WaitingForInventory → Failed</c> and cancels the Order through its own domain transition. Duplicate
/// results on the terminal state are idempotent no-ops, results on the opposite terminal state are
/// conflicting rejections, results on a non-terminal state are acknowledged anomalies, and a result for
/// an unknown saga/order throws (anomaly → retry/dead-letter). The transition stages are only visible
/// after the caller commits, and the failure path stages the <c>orders.order_cancelled</c> outbox record
/// atomically with the Order cancellation.
/// </summary>
public class OrderSagaResultTransitionTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset ResultAtUtc = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid ReservationId = Guid.NewGuid();

    [Fact]
    public async Task ApplyReservationSuccess_TransitionsSagaToCompleted_AndLeavesOrderPending()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var result = await orchestrator.ApplyReservationSuccessAsync(
            order.Id, ReservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);

        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Contains(ReservationId, saga.ReservationIds);

        await using var freshContext = CreateContext();
        Assert.Equal(OrderStatus.Pending, (await freshContext.Orders.SingleAsync()).Status);
        Assert.Equal(0, await freshContext.OutboxMessages.CountAsync(
            message => message.Type == OrderCancelledIntegrationEvent.EventType));
        // Phase 10.3: payment initiation is staged via Orders outbox
        Assert.Equal(1, await freshContext.OutboxMessages.CountAsync(
            message => message.Type == FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.CreatePaymentRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task ApplyReservationFailure_TransitionsSagaToFailed_CancelsOrder_AndStagesCancelledOutbox()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var result = await orchestrator.ApplyReservationFailureAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc);

        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Failed, saga.State);

        await using var freshContext = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await freshContext.Orders.SingleAsync()).Status);

        var cancelled = await freshContext.OutboxMessages
            .Where(message => message.Type == OrderCancelledIntegrationEvent.EventType)
            .ToListAsync();
        var record = Assert.Single(cancelled);
        Assert.Equal(OutboxStatus.Pending, record.Status);
        Assert.Equal(0, record.RetryCount);
    }

    [Fact]
    public async Task ApplyReservationSuccess_OnDuplicate_IsNoOp()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var correlationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, ReservationId, correlationId, Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        var versionAfterFirst = (await context.OrderSagas.SingleAsync()).Version;

        var duplicate = await orchestrator.ApplyReservationSuccessAsync(
            order.Id, ReservationId, correlationId, Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Equal(versionAfterFirst, saga.Version);
        Assert.Single(saga.ReservationIds);
        // Duplicate must not stage a second payment request
        await using var freshContext = CreateContext();
        Assert.Equal(1, await freshContext.OutboxMessages.CountAsync(
            message => message.Type == FluxCommerce.Modules.Payments.Contracts.IntegrationEvents.CreatePaymentRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task ApplyReservationFailure_OnDuplicate_IsNoOp_AndCancelsOnlyOnce()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var correlationId = Guid.NewGuid();
        await orchestrator.ApplyReservationFailureAsync(
            order.Id, correlationId, Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc);
        await context.SaveChangesAsync();

        var versionAfterFirst = (await context.OrderSagas.SingleAsync()).Version;

        var duplicate = await orchestrator.ApplyReservationFailureAsync(
            order.Id, correlationId, Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc);
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Failed, saga.State);
        Assert.Equal(versionAfterFirst, saga.Version);

        await using var freshContext = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await freshContext.Orders.SingleAsync()).Status);
        Assert.Equal(1, await freshContext.OutboxMessages.CountAsync(
            message => message.Type == OrderCancelledIntegrationEvent.EventType));
    }

    [Fact]
    public async Task ApplyReservationSuccess_OnFailedSaga_IsConflicting()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        await orchestrator.ApplyReservationFailureAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc);
        await context.SaveChangesAsync();

        var conflicting = await orchestrator.ApplyReservationSuccessAsync(
            order.Id, ReservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Conflicting, conflicting.Outcome);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Failed, saga.State);
        Assert.Empty(saga.ReservationIds);

        await using var freshContext = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await freshContext.Orders.SingleAsync()).Status);
        Assert.Equal(1, await freshContext.OutboxMessages.CountAsync(
            message => message.Type == OrderCancelledIntegrationEvent.EventType));
    }

    [Fact]
    public async Task ApplyReservationFailure_OnCompletedSaga_IsConflicting()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        await orchestrator.ApplyReservationSuccessAsync(order.Id, ReservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        // After Phase 10.3, success leaves saga in WaitingForPayment, not Completed.
        // Inventory failure after waiting for payment is not the expected failed-reservation path
        // — it should be rejected (no OrderCancelled, saga stays WaitingForPayment).
        var conflicting = await orchestrator.ApplyReservationFailureAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc);
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Invalid, conflicting.Outcome);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);

        await using var freshContext = CreateContext();
        Assert.Equal(OrderStatus.Pending, (await freshContext.Orders.SingleAsync()).Status);
        Assert.Equal(0, await freshContext.OutboxMessages.CountAsync(
            message => message.Type == OrderCancelledIntegrationEvent.EventType));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    public async Task ApplyReservationResult_OnStartedSaga_IsInvalid(string resultKind)
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));

        var startedSaga = OrderSaga.Start(Guid.NewGuid(), order.Id, Guid.NewGuid(), PlacedAtUtc);
        context.OrderSagas.Add(startedSaga);
        await context.SaveChangesAsync();

        var orchestrator = CreateOrchestrator(context);

        var result = resultKind == "success"
            ? await orchestrator.ApplyReservationSuccessAsync(order.Id, ReservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc)
            : await orchestrator.ApplyReservationFailureAsync(
                order.Id, Guid.NewGuid(), Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc);

        Assert.Equal(OrderSagaTransitionOutcome.Invalid, result.Outcome);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Started, saga.State);
        Assert.Equal(OrderSaga.InitialVersion, saga.Version);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    public async Task ApplyReservationResult_WhenSagaMissing_Throws(string resultKind)
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);

        var exception = resultKind == "success"
            ? await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.ApplyReservationSuccessAsync(order.Id, ReservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc))
            : await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.ApplyReservationFailureAsync(
                    order.Id, Guid.NewGuid(), Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc));

        Assert.Contains("no saga exists", exception.Message);
        Assert.Empty(await context.OrderSagas.ToListAsync());
    }

    [Fact]
    public async Task ApplyReservationFailure_WhenOrderMissing_Throws()
    {
        await using var context = CreateContext();

        var saga = OrderSaga.Start(Guid.NewGuid(), OrderId.Create(), Guid.NewGuid(), PlacedAtUtc);
        var transition = saga.RequestReservation(PlacedAtUtc);
        Assert.True(transition.IsSuccess);

        context.OrderSagas.Add(saga);
        await context.SaveChangesAsync();

        var orchestrator = CreateOrchestrator(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ApplyReservationFailureAsync(
                saga.OrderId, Guid.NewGuid(), Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc));

        Assert.Contains("the order does not exist", exception.Message);
    }

    [Fact]
    public async Task ApplyReservationFailure_OnAlreadyCancelledOrder_StillAppliesSagaFailure()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var cancel = order.Cancel(ResultAtUtc);
        Assert.True(cancel.IsSuccess);
        await context.SaveChangesAsync();

        var result = await orchestrator.ApplyReservationFailureAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc);
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Failed, saga.State);

        await using var freshContext = CreateContext();
        Assert.Equal(OrderStatus.Cancelled, (await freshContext.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApplyReservationFailure_StagesSagaAndOrder_OnlyAfterCommit()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        await orchestrator.ApplyReservationFailureAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), "Inventory.InsufficientStock", ResultAtUtc);

        // Nothing was committed: a fresh context must not see the transition or the cancellation.
        await using var uncommittedContext = CreateContext();
        Assert.Equal(OrderSagaState.WaitingForInventory, (await uncommittedContext.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Pending, (await uncommittedContext.Orders.SingleAsync()).Status);
        Assert.Equal(0, await uncommittedContext.OutboxMessages.CountAsync(
            message => message.Type == OrderCancelledIntegrationEvent.EventType));
    }

    [Fact]
    public async Task ApplyReservationSuccess_IncrementsSagaVersion_ExactlyOnce()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        await orchestrator.ApplyReservationSuccessAsync(order.Id, ReservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        // Initial 1 → RequestReservation 2 → Complete 3: the outcome transition bumped the version once.
        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(3, saga.Version);
        Assert.Equal(ResultAtUtc, saga.UpdatedAtUtc);
    }

    [Fact]
    public async Task ApplyReservationSuccess_ConcurrentResults_OnlyOneCommitSucceeds()
    {
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));

        await using (var seedContext = CreateContext())
        {
            var seedingOrchestrator = CreateOrchestrator(seedContext);
            var start = await seedingOrchestrator.StartFromOrderCreatedAsync(
                order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
            Assert.True(start.SagaCreated);
            await seedContext.SaveChangesAsync();
        }

        // Two contexts both read the same WaitingForInventory saga at the same version and each
        // applies the success transition with a distinct reservation id (plan §13.8.6 concurrent
        // result scenario). The first SaveChanges commits; the second is rejected by the saga's
        // optimistic concurrency token, so the concurrent results produce exactly one business
        // effect: a single WaitingForPayment transition and one recorded reservation.
        await using var contextA = CreateContext();
        await using var contextB = CreateContext();
        var orchestratorA = CreateOrchestrator(contextA);
        var orchestratorB = CreateOrchestrator(contextB);

        var resultA = await orchestratorA.ApplyReservationSuccessAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);
        var resultB = await orchestratorB.ApplyReservationSuccessAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);

        Assert.Equal(OrderSagaTransitionOutcome.Applied, resultA.Outcome);
        Assert.Equal(OrderSagaTransitionOutcome.Applied, resultB.Outcome);

        await contextA.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextB.SaveChangesAsync());

        await using var freshContext = CreateContext();
        var saga = await freshContext.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Single(saga.ReservationIds);
    }

    private async Task<OrderSagaOrchestrator> CreateWaitingSagaAsync(OrdersDbContext context, Order order)
    {
        var orchestrator = CreateOrchestrator(context);
        var start = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);

        Assert.True(start.SagaCreated);
        await context.SaveChangesAsync();
        Assert.Equal(OrderSagaState.WaitingForInventory, (await context.OrderSagas.SingleAsync()).State);

        return orchestrator;
    }
}