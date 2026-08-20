using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.UnitTests.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

/// <summary>
/// Phase 8.7: the Orders compensation consumer is a thin adapter that reacts to
/// <c>orders.order_cancelled</c> and delegates the compensation release to the saga orchestrator. A
/// structurally valid event stages the release commands for the order's recorded reservations; a
/// malformed one throws before the orchestrator is ever reached (the consumer infrastructure retries
/// then dead-letters, per Phase 7.7); an order with no saga or no recorded reservations is a benign
/// no-op.
/// </summary>
public class OrderCancelledSagaConsumerTests : OrderSagaTestBase
{
    [Fact]
    public void HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Orders.RequestInventoryRelease", OrderCancelledSagaConsumer.StableHandlerName);
    }

    [Fact]
    public async Task Cancelled_ValidEvent_RequestsRelease_ForRecordedReservations()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);

        var start = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        Assert.True(start.SagaCreated);
        await context.SaveChangesAsync();

        await orchestrator.ApplyReservationSuccessAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        var consumer = new OrderCancelledSagaConsumer(
            orchestrator,
            NullLogger<OrderCancelledSagaConsumer>.Instance);
        var @event = new OrderCancelledIntegrationEvent(
            order.Id.Value,
            Guid.NewGuid(),
            "ORD-000001",
            PlacedAtUtc)
        {
            CorrelationId = Guid.NewGuid()
        };

        await consumer.HandleAsync(@event, EnvelopeFrom(@event));
        await context.SaveChangesAsync();

        Assert.Single(await context.OutboxMessages
            .Where(message => message.Type == InventoryReleaseRequestedIntegrationEvent.EventType)
            .ToListAsync());
    }

    [Fact]
    public async Task Cancelled_OrderWithoutSaga_IsBenignNoOp()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        var consumer = new OrderCancelledSagaConsumer(
            orchestrator,
            NullLogger<OrderCancelledSagaConsumer>.Instance);
        var @event = new OrderCancelledIntegrationEvent(
            order.Id.Value,
            Guid.NewGuid(),
            "ORD-000002",
            PlacedAtUtc)
        {
            CorrelationId = Guid.NewGuid()
        };

        await consumer.HandleAsync(@event, EnvelopeFrom(@event));
        await context.SaveChangesAsync();

        Assert.Equal(0, await context.OutboxMessages.CountAsync(
            message => message.Type == InventoryReleaseRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task Cancelled_EmptyOrderId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new OrderCancelledSagaConsumer(
            orchestrator,
            NullLogger<OrderCancelledSagaConsumer>.Instance);
        var @event = new OrderCancelledIntegrationEvent(
            Guid.Empty,
            Guid.NewGuid(),
            "ORD-000003",
            PlacedAtUtc);

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    private static readonly DateTimeOffset ResultAtUtc = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static IntegrationEventEnvelope EnvelopeFrom(OrderCancelledIntegrationEvent @event) =>
        IntegrationEventEnvelope.From(@event, OrderCancelledIntegrationEvent.EventType);
}