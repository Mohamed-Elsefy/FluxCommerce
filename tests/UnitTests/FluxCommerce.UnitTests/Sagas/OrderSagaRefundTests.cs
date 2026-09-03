using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

public class OrderSagaRefundTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private static OrderSaga CreateCompletedSaga()
    {
        var saga = OrderSaga.Start(Guid.NewGuid(), OrderId.Create(), Guid.NewGuid(), UtcNow);
        saga.RequestReservation(UtcNow);
        saga.MarkWaitingForPayment(UtcNow.AddMinutes(1));
        saga.SucceedPayment(UtcNow.AddMinutes(2));
        Assert.Equal(OrderSagaState.Completed, saga.State);
        return saga;
    }

    [Fact]
    public void RequestRefund_FromCompleted_ToRefunding_IsAllowed()
    {
        var saga = CreateCompletedSaga();
        var result = saga.RequestRefund(UtcNow.AddMinutes(3));
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Refunding, saga.State);
    }

    [Fact]
    public void RequestRefund_OnRefunding_IsIdempotent()
    {
        var saga = CreateCompletedSaga();
        saga.RequestRefund(UtcNow.AddMinutes(3));
        var version = saga.Version;
        var result = saga.RequestRefund(UtcNow.AddMinutes(4));
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.Refunding, saga.State);
        Assert.Equal(version, saga.Version);
    }

    [Fact]
    public void HandlePaymentRefunded_FromRefunding_ToReleasingInventory_IsAllowed()
    {
        var saga = CreateCompletedSaga();
        saga.RequestRefund(UtcNow.AddMinutes(3));
        var result = saga.HandlePaymentRefunded(UtcNow.AddMinutes(4));
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
    }

    [Fact]
    public void HandlePaymentRefunded_OnReleasingInventory_IsIdempotent()
    {
        var saga = CreateCompletedSaga();
        saga.RequestRefund(UtcNow.AddMinutes(3));
        saga.HandlePaymentRefunded(UtcNow.AddMinutes(4));
        var version = saga.Version;
        var result = saga.HandlePaymentRefunded(UtcNow.AddMinutes(5));
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        Assert.Equal(version, saga.Version);
    }

    [Theory]
    [InlineData(OrderSagaState.Started)]
    [InlineData(OrderSagaState.WaitingForInventory)]
    [InlineData(OrderSagaState.WaitingForPayment)]
    [InlineData(OrderSagaState.Failed)]
    public void RequestRefund_OnInvalidState_IsRejected(OrderSagaState state)
    {
        var saga = OrderSaga.Hydrate(Guid.NewGuid(), OrderId.Create(), Guid.NewGuid(), state, OrderSaga.InitialVersion, UtcNow, UtcNow);
        var result = saga.RequestRefund(UtcNow);
        Assert.True(result.IsFailure);
        Assert.Equal(state, saga.State);
    }

    [Fact]
    public void FullRefundFlow_CompletedToRefundingToReleasingInventoryToCancelledToCompleted()
    {
        var saga = CreateCompletedSaga();
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.True(saga.RequestRefund(UtcNow.AddMinutes(3)).IsSuccess);
        Assert.Equal(OrderSagaState.Refunding, saga.State);
        Assert.True(saga.HandlePaymentRefunded(UtcNow.AddMinutes(4)).IsSuccess);
        Assert.Equal(OrderSagaState.ReleasingInventory, saga.State);
        Assert.True(saga.MarkInventoryReleased(UtcNow.AddMinutes(5)).IsSuccess);
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.True(saga.FinalizeCancellation(UtcNow.AddMinutes(6)).IsSuccess);
        Assert.Equal(OrderSagaState.Completed, saga.State);
    }
}
