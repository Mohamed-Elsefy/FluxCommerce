using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 8.3: the saga consumer is a thin adapter that boots the orchestrator and propagates the
/// Phase 7.8 trace identifiers from the consumed message to the produced reservation commands.
/// </summary>
public class OrderCreatedSagaConsumerTests : OrderSagaTestBase
{
    [Fact]
    public void HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Orders.RunOrderSaga", OrderCreatedSagaConsumer.StableHandlerName);
    }

    [Fact]
    public async Task HandleAsync_StartsSaga_AndPropagatesEnvelopeTraceIdentifiers()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));

        var consumer = new OrderCreatedSagaConsumer(CreateOrchestrator(context));

        var @event = new OrderCreatedIntegrationEvent(
            order.Id.Value,
            order.CustomerId.Value,
            order.OrderNumber.Value,
            PlacedAtUtc);

        var envelope = IntegrationEventEnvelope.From(@event, OrderCreatedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);

        await context.SaveChangesAsync();

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(order.Id, saga.OrderId);
        Assert.Equal(envelope.CorrelationId, saga.CorrelationId);

        var message = await context.OutboxMessages.SingleAsync(
            message => message.Type == InventoryReservationRequestedIntegrationEvent.EventType);
        Assert.Equal(envelope.CorrelationId, message.CorrelationId);
        Assert.Equal(envelope.EventId, message.CausationId);
    }
}