using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.UnitTests.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

public class InventoryReleasedConsumerTests : OrderSagaTestBase
{
    [Fact]
    public void HandlerName_IsStable() => Assert.Equal("Orders.ApplyInventoryReleased", InventoryReleasedConsumer.StableHandlerName);

    [Fact]
    public async Task HandleAsync_ValidEvent_AppliesCompensationTransition()
    {
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(2));
        await ctx.SaveChangesAsync();

        var consumer = new InventoryReleasedConsumer(orchestrator, NullLogger<InventoryReleasedConsumer>.Instance);
        var @event = new InventoryReleasedIntegrationEvent(reservationId, order.Id.Value, "SKU-1", 1, DateTimeOffset.UtcNow.AddMinutes(30), InventoryReservationStatus.Released, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReleasedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);
        await ctx.SaveChangesAsync();

        Assert.Equal(FluxCommerce.Modules.Orders.Domain.Enums.OrderSagaState.Completed, (await ctx.OrderSagas.SingleAsync()).State);
        Assert.Equal(FluxCommerce.Modules.Orders.Domain.Enums.OrderStatus.Cancelled, (await ctx.Orders.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HandleAsync_InvalidEvent_Throws(bool emptyReservation, bool emptyOrder)
    {
        await using var ctx = CreateContext();
        var orchestrator = CreateOrchestrator(ctx);
        var consumer = new InventoryReleasedConsumer(orchestrator, NullLogger<InventoryReleasedConsumer>.Instance);
        var @event = new InventoryReleasedIntegrationEvent(
            emptyReservation ? Guid.Empty : Guid.NewGuid(),
            emptyOrder ? Guid.Empty : Guid.NewGuid(),
            "SKU-1", 1, DateTimeOffset.UtcNow.AddMinutes(30), InventoryReservationStatus.Released, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1);
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReleasedIntegrationEvent.EventType);
        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, envelope));
    }
}
