using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Phase 8.4: the Inventory reservation-request consumer is a thin adapter that maps each saga-issued
/// <c>inventory.reservation_requested</c> event to the <see cref="ReserveInventoryCommand"/> use case
/// and delegates the business operation to the Inventory application handler. A failed reservation is
/// surfaced as an exception so the delivery is never acknowledged without a reservation (Phase 8.5
/// replaces the throw with the reservation-failed result event).
/// </summary>
public class InventoryReservationRequestedConsumerTests
{
    [Fact]
    public void HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Inventory.ReserveInventory", InventoryReservationRequestedConsumer.StableHandlerName);
    }

    [Fact]
    public async Task HandleAsync_MapsEventToReservationCommand_AndInvokesUseCase()
    {
        var orderId = Guid.NewGuid();
        var expiresAt = new DateTimeOffset(2026, 8, 19, 10, 30, 0, TimeSpan.Zero);
        var readModel = CreateReadModel(orderId, "SKU-1", expiresAt);
        var handler = new Mock<ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReserveInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(readModel));
        var consumer = new InventoryReservationRequestedConsumer(handler.Object);

        var @event = new InventoryReservationRequestedIntegrationEvent(orderId, "SKU-1", 2, expiresAt)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReservationRequestedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);

        handler.Verify(h => h.HandleAsync(It.IsAny<ReserveInventoryCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        handler.Verify(
            h => h.HandleAsync(
                It.Is<ReserveInventoryCommand>(command =>
                    command.OrderId == orderId
                    && command.Sku == "SKU-1"
                    && command.Quantity == 2
                    && command.ExpiresAt == expiresAt),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulReservation_CompletesWithoutError()
    {
        var orderId = Guid.NewGuid();
        var expiresAt = new DateTimeOffset(2026, 8, 19, 10, 30, 0, TimeSpan.Zero);
        var handler = new Mock<ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReserveInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateReadModel(orderId, "SKU-1", expiresAt)));
        var consumer = new InventoryReservationRequestedConsumer(handler.Object);

        var @event = new InventoryReservationRequestedIntegrationEvent(orderId, "SKU-1", 1, expiresAt)
        {
            EventId = Guid.NewGuid()
        };

        await consumer.HandleAsync(@event, IntegrationEventEnvelope.From(@event, InventoryReservationRequestedIntegrationEvent.EventType));

        handler.Verify(h => h.HandleAsync(It.IsAny<ReserveInventoryCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_FailedReservation_Throws_SoAttemptIsNeverAckedAsSuccess()
    {
        var orderId = Guid.NewGuid();
        var expiresAt = new DateTimeOffset(2026, 8, 19, 10, 30, 0, TimeSpan.Zero);
        var handler = new Mock<ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReserveInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<InventoryReservationReadModel>(
                Error.Conflict("Inventory.InsufficientStock", "Not enough stock available.")));
        var consumer = new InventoryReservationRequestedConsumer(handler.Object);

        var @event = new InventoryReservationRequestedIntegrationEvent(orderId, "SKU-1", 99, expiresAt)
        {
            EventId = Guid.NewGuid()
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => consumer.HandleAsync(@event, IntegrationEventEnvelope.From(@event, InventoryReservationRequestedIntegrationEvent.EventType)));

        Assert.Contains("Inventory.InsufficientStock", exception.Message);
        Assert.Contains(orderId.ToString(), exception.Message);
    }

    private static InventoryReservationReadModel CreateReadModel(Guid orderId, string sku, DateTimeOffset expiresAt) =>
        new(
            Guid.NewGuid(),
            orderId,
            sku,
            1,
            InventoryReservationStatus.Pending,
            expiresAt,
            new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
            InventoryReservation.InitialVersion);
}