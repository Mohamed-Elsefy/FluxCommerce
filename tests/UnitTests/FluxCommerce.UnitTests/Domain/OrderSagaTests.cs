using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Errors;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderSagaTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 19, 8, 15, 0, TimeSpan.Zero);

    private static OrderSaga CreateStartedSaga() =>
        OrderSaga.Start(Guid.NewGuid(), OrderId.Create(), Guid.NewGuid(), UtcNow);

    // ---- Creation ----

    [Fact]
    public void Start_CreatesSagaInStartedStateWithInitialVersion()
    {
        var saga = CreateStartedSaga();

        Assert.Equal(OrderSagaState.Started, saga.State);
        Assert.Equal(OrderSaga.InitialVersion, saga.Version);
        Assert.Equal(UtcNow, saga.CreatedAtUtc);
        Assert.Equal(UtcNow, saga.UpdatedAtUtc);
    }

    [Fact]
    public void Start_RejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderSaga.Start(Guid.Empty, OrderId.Create(), Guid.NewGuid(), UtcNow));
    }

    [Fact]
    public void Start_RejectsNullOrderId()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OrderSaga.Start(Guid.NewGuid(), null!, Guid.NewGuid(), UtcNow));
    }

    [Fact]
    public void Start_RejectsDefaultTimestamp()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderSaga.Start(Guid.NewGuid(), OrderId.Create(), Guid.NewGuid(), default));
    }

    // ---- Started -> WaitingForInventory ----

    [Fact]
    public void RequestReservation_StartedToWaitingForInventory_IsAllowed()
    {
        var saga = CreateStartedSaga();
        var later = UtcNow.AddMinutes(1);

        var result = saga.RequestReservation(later);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.WaitingForInventory, saga.State);
        Assert.Equal(OrderSaga.InitialVersion + 1, saga.Version);
        Assert.Equal(later, saga.UpdatedAtUtc);
    }

    [Fact]
    public void RequestReservation_OnWaitingForSaga_IsIdempotent()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        var versionBefore = saga.Version;

        var result = saga.RequestReservation(UtcNow.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.WaitingForInventory, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void RequestReservation_OnCompletedSaga_IsRejected()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        saga.Complete(UtcNow.AddMinutes(1));

        var result = saga.RequestReservation(UtcNow.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderSagaState.Completed, saga.State);
    }

    [Fact]
    public void RequestReservation_OnFailedSaga_IsRejected()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        saga.Fail(UtcNow.AddMinutes(1));

        var result = saga.RequestReservation(UtcNow.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderSagaState.Failed, saga.State);
    }

    // ---- WaitingForInventory -> Completed ----

    [Fact]
    public void Complete_WaitingForInventoryToCompleted_IsAllowed()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        var later = UtcNow.AddMinutes(1);

        var result = saga.Complete(later);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(OrderSaga.InitialVersion + 2, saga.Version);
        Assert.Equal(later, saga.UpdatedAtUtc);
    }

    [Fact]
    public void Complete_OnCompletedSaga_IsIdempotent()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        saga.Complete(UtcNow.AddMinutes(1));
        var versionBefore = saga.Version;

        var result = saga.Complete(UtcNow.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void Complete_OnStartedSaga_IsRejected()
    {
        var saga = CreateStartedSaga();

        var result = saga.Complete(UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderSagaState.Started, saga.State);
    }

    [Fact]
    public void Complete_OnFailedSaga_IsRejected()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        saga.Fail(UtcNow.AddMinutes(1));

        var result = saga.Complete(UtcNow.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderSagaState.Failed, saga.State);
    }

    // ---- WaitingForInventory -> Failed ----

    [Fact]
    public void Fail_WaitingForInventoryToFailed_IsAllowed()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        var later = UtcNow.AddMinutes(1);

        var result = saga.Fail(later);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Failed, saga.State);
        Assert.Equal(OrderSaga.InitialVersion + 2, saga.Version);
        Assert.Equal(later, saga.UpdatedAtUtc);
    }

    [Fact]
    public void Fail_OnFailedSaga_IsIdempotent()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        saga.Fail(UtcNow.AddMinutes(1));
        var versionBefore = saga.Version;

        var result = saga.Fail(UtcNow.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Failed, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void Fail_OnStartedSaga_IsRejected()
    {
        var saga = CreateStartedSaga();

        var result = saga.Fail(UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderSagaState.Started, saga.State);
    }

    [Fact]
    public void Fail_OnCompletedSaga_IsRejected()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        saga.Complete(UtcNow.AddMinutes(1));

        var result = saga.Fail(UtcNow.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderSagaState.Completed, saga.State);
    }

    // ---- Hydration ----

    [Fact]
    public void Hydrate_RestoresStateTimestampsAndVersion()
    {
        var id = Guid.NewGuid();
        var orderId = OrderId.Create();
        var correlationId = Guid.NewGuid();

        var saga = OrderSaga.Hydrate(
            id,
            orderId,
            correlationId,
            OrderSagaState.WaitingForInventory,
            4,
            UtcNow,
            UtcNow.AddMinutes(3));

        Assert.Equal(id, saga.Id);
        Assert.Equal(orderId, saga.OrderId);
        Assert.Equal(correlationId, saga.CorrelationId);
        Assert.Equal(OrderSagaState.WaitingForInventory, saga.State);
        Assert.Equal(4, saga.Version);
        Assert.Equal(UtcNow, saga.CreatedAtUtc);
        Assert.Equal(UtcNow.AddMinutes(3), saga.UpdatedAtUtc);
    }

    [Fact]
    public void Hydrate_RejectsVersionBelowInitial()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderSaga.Hydrate(
                Guid.NewGuid(),
                OrderId.Create(),
                Guid.NewGuid(),
                OrderSagaState.Started,
                0,
                UtcNow,
                UtcNow));
    }

    [Fact]
    public void Hydrate_RejectsUpdatedBeforeCreated()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderSaga.Hydrate(
                Guid.NewGuid(),
                OrderId.Create(),
                Guid.NewGuid(),
                OrderSagaState.Started,
                OrderSaga.InitialVersion,
                UtcNow,
                UtcNow.AddMinutes(-1)));
    }

    // ---- Phase 11.5 ApplyReservationExpired ----

    [Fact]
    public void ApplyReservationExpired_WaitingForPaymentToCancelled_IsAllowed()
    {
        var saga = CreateWaitingForPaymentSaga();
        var versionBefore = saga.Version;
        var later = UtcNow.AddMinutes(5);

        var result = saga.ApplyReservationExpired(later);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Equal(versionBefore + 1, saga.Version);
        Assert.Equal(later, saga.UpdatedAtUtc);
    }

    [Fact]
    public void ApplyReservationExpired_OnCompleted_IsSafelyIgnored_NoBackwardMove()
    {
        var saga = CreateCompletedSaga();
        var versionBefore = saga.Version;

        var result = saga.ApplyReservationExpired(UtcNow.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void ApplyReservationExpired_OnFailed_IsSafelyIgnored_NoBackwardMove()
    {
        var saga = CreateFailedSaga();
        var versionBefore = saga.Version;

        var result = saga.ApplyReservationExpired(UtcNow.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Failed, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void ApplyReservationExpired_OnCancelled_IsSafelyIgnored_NoBackwardMove()
    {
        var saga = CreateCancelledSaga();
        var versionBefore = saga.Version;

        var result = saga.ApplyReservationExpired(UtcNow.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void ApplyReservationExpired_OnReleasingInventory_IsSafelyIgnored()
    {
        var saga = CreateReleasingInventorySaga();
        var versionBefore = saga.Version;

        var result = saga.ApplyReservationExpired(UtcNow.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void ApplyReservationExpired_OnRefunding_IsSafelyIgnored()
    {
        var saga = CreateRefundingSaga();
        var versionBefore = saga.Version;

        var result = saga.ApplyReservationExpired(UtcNow.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Refunding, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void ApplyReservationExpired_OnWaitingForInventory_IsSafelyIgnored()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        var versionBefore = saga.Version;

        var result = saga.ApplyReservationExpired(UtcNow.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.WaitingForInventory, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void ApplyReservationExpired_OnStarted_IsRejected()
    {
        var saga = CreateStartedSaga();
        var versionBefore = saga.Version;

        var result = saga.ApplyReservationExpired(UtcNow.AddMinutes(5));

        Assert.True(result.IsFailure);
        Assert.Equal(OrderSagaErrors.InvalidStateTransition, result.Error);
        Assert.Equal(OrderSagaState.Started, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Theory]
    [InlineData(OrderSagaState.Completed)]
    [InlineData(OrderSagaState.Failed)]
    [InlineData(OrderSagaState.Cancelled)]
    [InlineData(OrderSagaState.ReleasingInventory)]
    [InlineData(OrderSagaState.Refunding)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    public void ApplyReservationExpired_OnIgnoreStates_DoesNotBumpVersion(OrderSagaState state)
    {
        var saga = CreateSagaInState(state);
        var versionBefore = saga.Version;

        var result = saga.ApplyReservationExpired(UtcNow.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(state, saga.State);
        Assert.Equal(versionBefore, saga.Version);
    }

    [Fact]
    public void ApplyReservationExpired_RejectsDefaultTimestamp()
    {
        var saga = CreateWaitingForPaymentSaga();

        Assert.Throws<ArgumentException>(() => saga.ApplyReservationExpired(default));
    }

    private OrderSaga CreateWaitingForPaymentSaga()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        saga.MarkWaitingForPayment(UtcNow.AddMinutes(1));
        saga.MarkPaymentRequested(Guid.NewGuid(), "pi_123", UtcNow.AddMinutes(2));
        return saga;
    }

    private OrderSaga CreateCompletedSaga()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.SucceedPayment(UtcNow.AddMinutes(3));
        return saga;
    }

    private OrderSaga CreateFailedSaga()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        saga.Fail(UtcNow.AddMinutes(1));
        return saga;
    }

    private OrderSaga CreateReleasingInventorySaga()
    {
        var saga = CreateWaitingForPaymentSaga();
        saga.FailPayment(UtcNow.AddMinutes(3), "Card.Declined");
        return saga;
    }

    private OrderSaga CreateCancelledSaga()
    {
        var saga = CreateReleasingInventorySaga();
        saga.MarkInventoryReleased(UtcNow.AddMinutes(4));
        return saga;
    }

    private OrderSaga CreateRefundingSaga()
    {
        var saga = CreateCompletedSaga();
        saga.RequestRefund(UtcNow.AddMinutes(4));
        return saga;
    }

    private OrderSaga CreateSagaInState(OrderSagaState state) => state switch
    {
        OrderSagaState.Completed => CreateCompletedSaga(),
        OrderSagaState.Failed => CreateFailedSaga(),
        OrderSagaState.Cancelled => CreateCancelledSaga(),
        OrderSagaState.ReleasingInventory => CreateReleasingInventorySaga(),
        OrderSagaState.Refunding => CreateRefundingSaga(),
        OrderSagaState.WaitingForInventory => CreateStartedSagaWithReservation(),
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private OrderSaga CreateStartedSagaWithReservation()
    {
        var saga = CreateStartedSaga();
        saga.RequestReservation(UtcNow);
        return saga;
    }
}