using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// The composite consumer is the single registered handler of
/// <c>orders.order_created</c>: it projects the order summary and boots the
/// saga atomically inside the same idempotent-processing boundary.
/// </summary>
public class OrderCreatedCompositeConsumerTests : OrderSagaTestBase
{
    [Fact]
    public void HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Orders.HandleOrderCreated", OrderCreatedCompositeConsumer.StableHandlerName);
    }

    [Fact]
    public async Task HandleAsync_ProjectsSummary_AndStartsSaga()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));

        var projectionService = new Mock<IOrderSummaryProjectionService>();
        var consumer = new OrderCreatedCompositeConsumer(
            projectionService.Object,
            CreateOrchestrator(context));

        var @event = new OrderCreatedIntegrationEvent(
            order.Id.Value,
            order.CustomerId.Value,
            order.OrderNumber.Value,
            PlacedAtUtc);

        var envelope = IntegrationEventEnvelope.From(@event, OrderCreatedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);

        await context.SaveChangesAsync();

        projectionService.Verify(
            s => s.ProjectAsync(order.Id.Value, envelope.CorrelationId, envelope.CausationId, It.IsAny<CancellationToken>()),
            Times.Once);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(order.Id, saga.OrderId);
        Assert.Equal(envelope.CorrelationId, saga.CorrelationId);

        var message = await context.OutboxMessages.SingleAsync(
            message => message.Type == InventoryReservationRequestedIntegrationEvent.EventType);
        Assert.Equal(envelope.CorrelationId, message.CorrelationId);
        Assert.Equal(envelope.EventId, message.CausationId);
    }
}
