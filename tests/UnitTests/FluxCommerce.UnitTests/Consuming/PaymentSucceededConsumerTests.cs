using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.UnitTests.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

public class PaymentSucceededConsumerTests : OrderSagaTestBase
{
    [Fact]
    public void HandlerName_IsStable()
    {
        Assert.Equal("Orders.ApplyPaymentSucceeded", PaymentSucceededConsumer.StableHandlerName);
    }

    [Fact]
    public async Task HandleAsync_ValidEvent_ConfirmsOrderAndCompletesSaga()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();

        var consumer = new PaymentSucceededConsumer(orchestrator, NullLogger<PaymentSucceededConsumer>.Instance);
        var @event = new PaymentSucceededIntegrationEvent(order.Id.Value, Guid.NewGuid(), "pi_test_123", 20m, "USD", "Succeeded", DateTimeOffset.UtcNow)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        var envelope = IntegrationEventEnvelope.From(@event, PaymentSucceededIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);
        await ctx.SaveChangesAsync();

        Assert.Equal(FluxCommerce.Modules.Orders.Domain.Enums.OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(FluxCommerce.Modules.Orders.Domain.Enums.OrderStatus.Confirmed, (await ctx.Orders.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HandleAsync_InvalidEvent_Throws(bool emptyOrder, bool emptyPayment)
    {
        await using var ctx = CreateContext();
        var orchestrator = CreateOrchestrator(ctx);
        var consumer = new PaymentSucceededConsumer(orchestrator, NullLogger<PaymentSucceededConsumer>.Instance);
        var @event = new PaymentSucceededIntegrationEvent(
            emptyOrder ? Guid.Empty : Guid.NewGuid(),
            emptyPayment ? Guid.Empty : Guid.NewGuid(),
            "pi_test",
            10m, "USD", "Succeeded", DateTimeOffset.UtcNow);
        var envelope = IntegrationEventEnvelope.From(@event, PaymentSucceededIntegrationEvent.EventType);
        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, envelope));
    }
}
