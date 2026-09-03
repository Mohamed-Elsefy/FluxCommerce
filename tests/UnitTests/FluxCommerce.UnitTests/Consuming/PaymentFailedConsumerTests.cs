using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.UnitTests.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

public class PaymentFailedConsumerTests : OrderSagaTestBase
{
    [Fact]
    public void HandlerName_IsStable() => Assert.Equal("Orders.ApplyPaymentFailed", PaymentFailedConsumer.StableHandlerName);

    [Fact]
    public async Task HandleAsync_ValidEvent_TransitionsToReleasingInventory_AndStagesRelease()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();

        var consumer = new PaymentFailedConsumer(orchestrator, NullLogger<PaymentFailedConsumer>.Instance);
        var @event = new PaymentFailedIntegrationEvent(order.Id.Value, Guid.NewGuid(), "pi_test", "card_declined", "Failed", DateTimeOffset.UtcNow)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        var envelope = IntegrationEventEnvelope.From(@event, PaymentFailedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);
        await ctx.SaveChangesAsync();

        Assert.Equal(FluxCommerce.Modules.Orders.Domain.Enums.OrderSagaState.ReleasingInventory, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.True(await ctx.OutboxMessages.AnyAsync(m => m.Type == "inventory.release_requested"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HandleAsync_InvalidEvent_Throws(bool emptyOrder, bool emptyReason)
    {
        await using var ctx = CreateContext();
        var orchestrator = CreateOrchestrator(ctx);
        var consumer = new PaymentFailedConsumer(orchestrator, NullLogger<PaymentFailedConsumer>.Instance);
        var @event = new PaymentFailedIntegrationEvent(
            emptyOrder ? Guid.Empty : Guid.NewGuid(),
            Guid.NewGuid(),
            "pi_test",
            emptyReason ? string.Empty : "reason",
            "Failed",
            DateTimeOffset.UtcNow);
        var envelope = IntegrationEventEnvelope.From(@event, PaymentFailedIntegrationEvent.EventType);
        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, envelope));
    }
}
