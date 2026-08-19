using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Messaging.Events;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.UnitTests.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

/// <summary>
/// Phase 8.5/8.6: the Orders result consumers (<c>inventory.reserved</c> / <c>inventory.reservation_failed</c>)
/// are thin adapters that validate the payload and delegate the saga transition to the orchestrator.
/// A structurally valid result event runs through the Orders idempotent-processing boundary and applies
/// the transition (saga Completed on success; saga Failed + Order cancelled on failure); a malformed one
/// throws before the orchestrator is ever reached (the consumer infrastructure retries then dead-letters,
/// per Phase 7.7).
/// </summary>
public class OrderResultConsumerTests : OrderSagaTestBase
{
    [Fact]
    public void InventoryReservedConsumer_HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Orders.ApplyReservationSuccess", InventoryReservedConsumer.StableHandlerName);
    }

    [Fact]
    public void InventoryReservationFailedConsumer_HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Orders.ApplyReservationFailure", InventoryReservationFailedConsumer.StableHandlerName);
    }

    [Fact]
    public async Task Reserved_ValidEvent_AppliesSuccessTransition()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(order.Id.Value);

        await consumer.HandleAsync(@event, EnvelopeFrom(@event));
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaState.Completed, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Pending, (await context.Orders.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Reserved_InvalidSku_Throws(string? sku)
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(Guid.NewGuid()) with { Sku = sku ?? string.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Reserved_EmptyReservationId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(Guid.NewGuid()) with { ReservationId = Guid.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Reserved_EmptyOrderId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Reserved_NonPositiveQuantity_Throws(int quantity)
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservedConsumer(
            orchestrator,
            NullLogger<InventoryReservedConsumer>.Instance);
        var @event = CreateReservedEvent(Guid.NewGuid()) with { Quantity = quantity };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Failed_ValidEvent_AppliesFailureTransition_AndCancelsOrder()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        var consumer = new InventoryReservationFailedConsumer(
            orchestrator,
            NullLogger<InventoryReservationFailedConsumer>.Instance);
        var @event = CreateFailedEvent(order.Id.Value);

        await consumer.HandleAsync(@event, EnvelopeFrom(@event));
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaState.Failed, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(OrderStatus.Cancelled, (await context.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Failed_EmptyOrderId_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationFailedConsumer(
            orchestrator,
            NullLogger<InventoryReservationFailedConsumer>.Instance);
        var @event = CreateFailedEvent(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Fact]
    public async Task Failed_EmptyReason_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationFailedConsumer(
            orchestrator,
            NullLogger<InventoryReservationFailedConsumer>.Instance);
        var @event = CreateFailedEvent(Guid.NewGuid()) with { Reason = string.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Failed_NonPositiveQuantity_Throws(int quantity)
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);
        var consumer = new InventoryReservationFailedConsumer(
            orchestrator,
            NullLogger<InventoryReservationFailedConsumer>.Instance);
        var @event = CreateFailedEvent(Guid.NewGuid()) with { Quantity = quantity };

        await Assert.ThrowsAsync<ArgumentException>(() => consumer.HandleAsync(@event, EnvelopeFrom(@event)));
    }

    private static InventoryReservedIntegrationEvent CreateReservedEvent(Guid orderId) =>
        new(
            Guid.NewGuid(),
            orderId,
            "SKU-RESULT",
            3,
            DateTimeOffset.UtcNow.AddMinutes(30),
            InventoryReservationStatus.Pending,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1);

    private static InventoryReservationFailedIntegrationEvent CreateFailedEvent(Guid orderId) =>
        new(orderId, "SKU-RESULT", 3, "Inventory.InsufficientStock");

    private static IntegrationEventEnvelope EnvelopeFrom(IIntegrationEvent @event) =>
        IntegrationEventEnvelope.From(@event, @event is InventoryReservedIntegrationEvent
            ? InventoryReservedIntegrationEvent.EventType
            : InventoryReservationFailedIntegrationEvent.EventType);
}