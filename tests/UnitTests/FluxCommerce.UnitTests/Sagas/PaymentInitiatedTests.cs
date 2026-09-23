using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// B1 coverage: <c>payments.payment_initiated</c> records the payment correlation on the saga
/// (<c>MarkPaymentRequested</c>) so refunds use tracked ids; mismatched payment results are
/// rejected as conflicting instead of confirming a stranger payment.
/// </summary>
public class PaymentInitiatedTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset InitiatedAt = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyPaymentInitiated_InWaitingForPayment_TracksPaymentIdWithoutVersionBump()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        var versionBefore = (await ctx.OrderSagas.SingleAsync()).Version;

        var paymentId = Guid.NewGuid();
        var result = await orchestrator.ApplyPaymentInitiatedAsync(order.Id, paymentId, "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Equal(paymentId, saga.PaymentId);
        Assert.Equal("pi_test_123", saga.ProviderPaymentIntentId);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public async Task ApplyPaymentInitiated_SamePaymentId_IsIdempotentApplied()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();

        var paymentId = Guid.NewGuid();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, paymentId, "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt);
        await ctx.SaveChangesAsync();
        var duplicate = await orchestrator.ApplyPaymentInitiatedAsync(order.Id, paymentId, "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, duplicate.Outcome);
        Assert.Equal(paymentId, (await ctx.OrderSagas.SingleAsync()).PaymentId);
    }

    [Fact]
    public async Task ApplyPaymentInitiated_DifferentPaymentId_ReturnsInvalid()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();

        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, Guid.NewGuid(), "pi_first", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt);
        await ctx.SaveChangesAsync();
        var conflicting = await orchestrator.ApplyPaymentInitiatedAsync(order.Id, Guid.NewGuid(), "pi_other", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Invalid, conflicting.Outcome);
        Assert.Equal("pi_first", (await ctx.OrderSagas.SingleAsync()).ProviderPaymentIntentId);
    }

    [Fact]
    public async Task ApplyPaymentInitiated_BeforeWaitingForPayment_ReturnsInvalid()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();

        var result = await orchestrator.ApplyPaymentInitiatedAsync(order.Id, Guid.NewGuid(), "pi_test", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt);

        Assert.Equal(OrderSagaTransitionOutcome.Invalid, result.Outcome);
        Assert.Null((await ctx.OrderSagas.SingleAsync()).PaymentId);
    }

    [Fact]
    public async Task ApplyPaymentSucceeded_WithMismatchedPaymentId_ReturnsConflictingWithoutConfirming()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, Guid.NewGuid(), "pi_tracked", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt);
        await ctx.SaveChangesAsync();

        var result = await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_stranger", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt.AddMinutes(1));
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Conflicting, result.Outcome);
        Assert.Equal(OrderSagaState.WaitingForPayment, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Pending, (await ctx.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApplyPaymentFailed_WithMismatchedPaymentId_ReturnsConflictingWithoutReleasing()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, Guid.NewGuid(), "pi_tracked", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt);
        await ctx.SaveChangesAsync();

        var result = await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_stranger", "card_declined", Guid.NewGuid(), Guid.NewGuid(), InitiatedAt.AddMinutes(1));
        await ctx.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Conflicting, result.Outcome);
        Assert.Equal(OrderSagaState.WaitingForPayment, (await ctx.OrderSagas.SingleAsync()).State);
    }

    [Fact]
    public async Task RequestRefund_WithoutTrackedPayment_ThrowsFailFast()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        // Succeed without a prior initiated: legacy untracked saga.
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, Guid.NewGuid(), "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(2));
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.RequestRefundAsync(order.Id, "downstream failure", Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task PaymentInitiatedConsumer_ValidEvent_AppliesTracking()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();

        var paymentId = Guid.NewGuid();
        var @event = new PaymentInitiatedIntegrationEvent(order.Id.Value, paymentId, "pi_consumer_1", InitiatedAt)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = InitiatedAt.UtcDateTime
        };
        var consumer = new PaymentInitiatedConsumer(orchestrator, NullLogger<PaymentInitiatedConsumer>.Instance);

        await consumer.HandleAsync(@event, IntegrationEventEnvelope.From(@event, PaymentInitiatedIntegrationEvent.EventType));
        await ctx.SaveChangesAsync();

        Assert.Equal(paymentId, (await ctx.OrderSagas.SingleAsync()).PaymentId);
    }
}
