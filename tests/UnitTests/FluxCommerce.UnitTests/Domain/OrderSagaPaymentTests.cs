using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Errors;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

/// <summary>
/// Phase 10.2: Payment and compensation state machine transitions for <see cref="OrderSaga"/>.
/// Covers valid transitions, invalid rejections, and idempotent duplicate handling
/// for all payment-related events.
/// </summary>
public class OrderSagaPaymentTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later1 = UtcNow.AddMinutes(1);
    private static readonly DateTimeOffset Later2 = UtcNow.AddMinutes(2);
    private static readonly DateTimeOffset Later3 = UtcNow.AddMinutes(3);
    private static readonly DateTimeOffset Later4 = UtcNow.AddMinutes(4);

    private static OrderSaga CreateStartedSaga() =>
        OrderSaga.Start(Guid.NewGuid(), OrderId.Create(), Guid.NewGuid(), UtcNow);

    private static OrderSaga CreateWaitingForInventorySaga()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        return saga;
    }

    private static OrderSaga CreateWaitingForPaymentSaga()
    {
        var saga = CreateWaitingForInventorySaga();
        var r = saga.MarkWaitingForPayment(Later1);
        Assert.True(r.IsSuccess);
        return saga;
    }

    private static OrderSaga CreateReleasingInventorySaga()
    {
        var saga = CreateWaitingForPaymentSaga();
        var r = saga.FailPayment(Later2, "insufficient_funds");
        Assert.True(r.IsSuccess);
        return saga;
    }

    private static OrderSaga CreateCancelledSaga()
    {
        var saga = CreateReleasingInventorySaga();
        var r = saga.MarkInventoryReleased(Later3);
        Assert.True(r.IsSuccess);
        return saga;
    }

    // ---- WaitingForInventory -> WaitingForPayment ----

    [Fact]
    public void MarkWaitingForPayment_FromWaitingForInventory_IsAllowed()
    {
        var saga = CreateWaitingForInventorySaga();
        var versionBefore = saga.Version;

        var result = saga.MarkWaitingForPayment(Later1);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Equal(versionBefore + 1, saga.Version);
        Assert.Equal(Later1, saga.UpdatedAtUtc);
    }

    [Fact]
    public void MarkWaitingForPayment_OnWaitingForPayment_IsIdempotent()
    {
        var saga = CreateWaitingForPaymentSaga();
        var versionBefore = saga.Version;

        var result = saga.MarkWaitingForPayment(Later2);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.Completed)]
    [InlineData(OrderSagaState.Failed)]
    [InlineData(OrderSagaState.ReleasingInventory)]
    [InlineData(OrderSagaState.Cancelled)]
    public void MarkWaitingForPayment_OnInvalidState_IsRejected(OrderSagaState state)
    {
        var saga = CreateSagaInState(state);

        var result = saga.MarkWaitingForPayment(Later1);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(state, saga.State);
    }

    // ---- MarkPaymentRequested ----

    [Fact]
    public void MarkPaymentRequested_InWaitingForPayment_RecordsPayment()
    {
        var saga = CreateWaitingForPaymentSaga();
        var paymentId = Guid.NewGuid();
        var intentId = "pi_test_123";

        var result = saga.MarkPaymentRequested(paymentId, intentId, Later2);

        Assert.True(result.IsSuccess);
        Assert.Equal(paymentId, saga.PaymentId);
        Assert.Equal(intentId, saga.ProviderPaymentIntentId);
        Assert.Equal(Later2, saga.PaymentRequestedAtUtc);
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
    }

    [Fact]
    public void MarkPaymentRequested_SamePaymentId_IsIdempotent()
    {
        var saga = CreateWaitingForPaymentSaga();
        var paymentId = Guid.NewGuid();
        saga.MarkPaymentRequested(paymentId, "pi_1", Later2);
        var versionBefore = saga.Version;

        var result = saga.MarkPaymentRequested(paymentId, "pi_1", Later3);

        Assert.True(result.IsSuccess);
        Assert.Equal(paymentId, saga.PaymentId);
        Assert.Equal(versionBefore, saga.Version);
        Assert.Equal(Later2, saga.PaymentRequestedAtUtc); // first write wins
    }

    [Fact]
    public void MarkPaymentRequested_DifferentPaymentId_IsRejected()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.MarkPaymentRequested(Guid.NewGuid(), "pi_1", Later2);

        var result = saga.MarkPaymentRequested(Guid.NewGuid(), "pi_2", Later3);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.Completed)]
    [InlineData(OrderSagaState.Failed)]
    [InlineData(OrderSagaState.ReleasingInventory)]
    [InlineData(OrderSagaState.Cancelled)]
    public void MarkPaymentRequested_OutsideWaitingForPayment_IsRejected(OrderSagaState state)
    {
        var saga = CreateSagaInState(state);
        var result = saga.MarkPaymentRequested(Guid.NewGuid(), "pi_1", Later1);
        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void MarkPaymentRequested_RejectsEmptyPaymentId()
    {
        var saga = CreateWaitingForPaymentSaga();
        Assert.Throws<ArgumentException>(() => saga.MarkPaymentRequested(Guid.Empty, "pi_1", Later2));
    }

    // ---- WaitingForPayment -> Completed (PaymentSucceeded) ----

    [Fact]
    public void SucceedPayment_FromWaitingForPayment_ToCompleted_IsAllowed()
    {
        var saga = CreateWaitingForPaymentSaga();
        var versionBefore = saga.Version;

        var result = saga.SucceedPayment(Later2);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionBefore + 1, saga.Version);
        Assert.Equal(Later2, saga.UpdatedAtUtc);
    }

    [Fact]
    public void SucceedPayment_OnCompleted_IsIdempotent()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.SucceedPayment(Later2);
        var versionBefore = saga.Version;

        var result = saga.SucceedPayment(Later3);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.Failed)]
    [InlineData(OrderSagaState.ReleasingInventory)]
    [InlineData(OrderSagaState.Cancelled)]
    public void SucceedPayment_OnInvalidState_IsRejected(OrderSagaState state)
    {
        var saga = CreateSagaInState(state);
        var result = saga.SucceedPayment(Later1);
        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(state, saga.State);
    }

    // ---- WaitingForPayment -> ReleasingInventory (PaymentFailed) ----

    [Fact]
    public void FailPayment_FromWaitingForPayment_ToReleasingInventory_IsAllowed()
    {
        var saga = CreateWaitingForPaymentSaga();
        var versionBefore = saga.Version;

        var result = saga.FailPayment(Later2, "card_declined");

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        Assert.Equal("card_declined", saga.PaymentFailureReason);
        Assert.Equal(versionBefore + 1, saga.Version);
    }

    [Fact]
    public void FailPayment_OnReleasingInventory_IsIdempotent()
    {
        var saga = CreateReleasingInventorySaga();
        var versionBefore = saga.Version;

        var result = saga.FailPayment(Later3, "card_declined");

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void FailPayment_OnCancelled_IsIdempotent()
    {
        var saga = CreateCancelledSaga();
        var versionBefore = saga.Version;

        var result = saga.FailPayment(Later4, "card_declined");

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.Completed)]
    [InlineData(OrderSagaState.Failed)]
    public void FailPayment_OnInvalidTerminal_IsRejected(OrderSagaState state)
    {
        var saga = CreateSagaInState(state);
        var result = saga.FailPayment(Later1, "reason");
        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void FailPayment_RejectsEmptyReason()
    {
        var saga = CreateWaitingForPaymentSaga();
        Assert.Throws<ArgumentException>(() => saga.FailPayment(Later2, ""));
        Assert.Throws<ArgumentException>(() => saga.FailPayment(Later2, "   "));
    }

    // ---- ReleasingInventory -> Cancelled ----

    [Fact]
    public void MarkInventoryReleased_FromReleasingInventory_ToCancelled_IsAllowed()
    {
        var saga = CreateReleasingInventorySaga();
        var versionBefore = saga.Version;

        var result = saga.MarkInventoryReleased(Later3);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Equal(versionBefore + 1, saga.Version);
    }

    [Fact]
    public void MarkInventoryReleased_OnCancelled_IsIdempotent()
    {
        var saga = CreateCancelledSaga();
        var versionBefore = saga.Version;

        var result = saga.MarkInventoryReleased(Later4);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void MarkInventoryReleased_OnCompleted_IsIdempotent()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.SucceedPayment(Later2);
        var versionBefore = saga.Version;

        var result = saga.MarkInventoryReleased(Later3);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.WaitingForPayment)]
    [InlineData(OrderSagaState.Failed)]
    public void MarkInventoryReleased_OnInvalidState_IsRejected(OrderSagaState state)
    {
        var saga = CreateSagaInState(state);
        var result = saga.MarkInventoryReleased(Later1);
        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
    }

    // ---- Cancelled -> Completed ----

    [Fact]
    public void FinalizeCancellation_FromCancelled_ToCompleted_IsAllowed()
    {
        var saga = CreateCancelledSaga();
        var versionBefore = saga.Version;

        var result = saga.FinalizeCancellation(Later4);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionBefore + 1, saga.Version);
    }

    [Fact]
    public void FinalizeCancellation_OnCompleted_IsIdempotent()
    {
        var saga = CreateCancelledSaga();
        saga.FinalizeCancellation(Later4);
        var versionBefore = saga.Version;

        var result = saga.FinalizeCancellation(Later4.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.WaitingForPayment)]
    [InlineData(OrderSagaState.ReleasingInventory)]
    [InlineData(OrderSagaState.Failed)]
    public void FinalizeCancellation_OnInvalidState_IsRejected(OrderSagaState state)
    {
        var saga = CreateSagaInState(state);
        var result = saga.FinalizeCancellation(Later1);
        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
    }

    // ---- Full path integration ----

    [Fact]
    public void FullSuccessPath_StartedToCompletedViaPayment_IsAllowed()
    {
        var saga = CreateStartedSaga();
        Assert.True(saga.RequestReservation(UtcNow).IsSuccess);
        Assert.True(saga.MarkWaitingForPayment(Later1).IsSuccess);
        Assert.True(saga.SucceedPayment(Later2).IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(OrderSaga.InitialVersion + 3, saga.Version);
    }

    [Fact]
    public void FullFailurePath_StartedToCompletedViaCompensation_IsAllowed()
    {
        var saga = CreateStartedSaga();
        Assert.True(saga.RequestReservation(UtcNow).IsSuccess);
        Assert.True(saga.MarkWaitingForPayment(Later1).IsSuccess);
        Assert.True(saga.FailPayment(Later2, "declined").IsSuccess);
        Assert.True(saga.MarkInventoryReleased(Later3).IsSuccess);
        Assert.True(saga.FinalizeCancellation(Later4).IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        // Started(1) -> WaitingForInventory(2) -> WaitingForPayment(3) -> ReleasingInventory(4) -> Cancelled(5) -> Completed(6)
        Assert.Equal(OrderSaga.InitialVersion + 5, saga.Version);
    }

    [Fact]
    public void DuplicatePaymentSucceeded_DoesNotThrow_AndStaysCompleted()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.SucceedPayment(Later2);
        var firstVersion = saga.Version;

        // Duplicate delivery
        var second = saga.SucceedPayment(Later3);
        var third = saga.SucceedPayment(Later3);

        Assert.True(second.IsSuccess);
        Assert.True(third.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(firstVersion, saga.Version);
    }

    [Fact]
    public void DuplicatePaymentFailed_DoesNotCreateDuplicateCompensation()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.FailPayment(Later2, "reason");
        var versionAfterFirst = saga.Version;

        var second = saga.FailPayment(Later3, "reason");
        Assert.True(second.IsSuccess);
        Assert.Equal(versionAfterFirst, saga.Version);
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
    }

    [Fact]
    public void OutOfOrder_PaymentSucceededBeforeInventory_IsRejected()
    {
        var saga = CreateStartedSaga();
        // Directly attempt payment success without inventory
        var result = saga.SucceedPayment(Later1);
        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaState.Started, saga.State);
    }

    [Fact]
    public void OutOfOrder_PaymentFailedAfterCompleted_IsRejected()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.SucceedPayment(Later2);
        var result = saga.FailPayment(Later3, "late_failure");
        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaState.Completed, saga.State);
    }

    [Fact]
    public void ConcurrentConflictingEvents_SucceedVsFail_PaymentFailedIsRejectedAfterSucceed()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.SucceedPayment(Later2);
        // Concurrent payment_failed arrives after succeeded
        var failResult = saga.FailPayment(Later3, "concurrent_fail");
        Assert.True(failResult.IsFailure);
    }

    // ---- Hydrate with payment fields ----

    [Fact]
    public void Hydrate_RestoresPaymentFields()
    {
        var id = Guid.NewGuid();
        var orderId = OrderId.Create();
        var correlationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var saga = OrderSaga.Hydrate(
            id,
            orderId,
            correlationId,
            OrderSagaState.WaitingForPayment,
            5,
            UtcNow,
            Later2,
            releaseRequestedAtUtc: null,
            paymentId: paymentId,
            providerPaymentIntentId: "pi_123",
            paymentFailureReason: null,
            paymentRequestedAtUtc: Later1);

        Assert.Equal(paymentId, saga.PaymentId);
        Assert.Equal("pi_123", saga.ProviderPaymentIntentId);
        Assert.Equal(Later1, saga.PaymentRequestedAtUtc);
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Equal(5, saga.Version);
    }

    [Fact]
    public void Hydrate_RejectsEmptyPaymentId()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderSaga.Hydrate(Guid.NewGuid(), OrderId.Create(), Guid.NewGuid(), OrderSagaState.WaitingForPayment, 1, UtcNow, UtcNow, paymentId: Guid.Empty));
    }

    [Fact]
    public void Hydrate_RejectsDefaultPaymentRequestedAtUtc()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderSaga.Hydrate(Guid.NewGuid(), OrderId.Create(), Guid.NewGuid(), OrderSagaState.WaitingForPayment, 1, UtcNow, UtcNow, paymentRequestedAtUtc: default(DateTimeOffset)));
    }

    // ---- Helper ----

    private static OrderSaga CreateSagaInState(OrderSagaState targetState)
    {
        var id = Guid.NewGuid();
        var orderId = OrderId.Create();
        var correlationId = Guid.NewGuid();

        // Create saga in target state via Hydrate to avoid transition restrictions
        return OrderSaga.Hydrate(
            id,
            orderId,
            correlationId,
            targetState,
            OrderSaga.InitialVersion,
            UtcNow,
            UtcNow);
    }
}
