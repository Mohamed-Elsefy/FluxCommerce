using System.Text.Json;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.Enums;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Infrastructure.Consuming;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Phase 8.4/8.5: the Inventory reservation-request consumer is a thin adapter that maps each
/// saga-issued <c>inventory.reservation_requested</c> event to the <see cref="ReserveInventoryCommand"/>
/// use case, delegates the business operation to the Inventory application handler, and stages the
/// result event (Phase 8.5): <c>inventory.reserved</c> on success or <c>inventory.reservation_failed</c>
/// on a business failure — both carrying the workflow correlation and the causal link back to the
/// consumed command. A business failure is a valid, acknowledged outcome (never a retry).
/// </summary>
public class InventoryReservationRequestedConsumerTests : IDisposable
{
    private static readonly DateTimeOffset ExpiresAt = new(2026, 8, 19, 10, 30, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public InventoryReservationRequestedConsumerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public void HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Inventory.ReserveInventory", InventoryReservationRequestedConsumer.StableHandlerName);
    }

    [Fact]
    public async Task HandleAsync_MapsEventToReservationCommand_AndInvokesUseCase()
    {
        var orderId = Guid.NewGuid();
        var readModel = CreateReadModel(orderId, "SKU-1", ExpiresAt);
        var handler = new Mock<ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReserveInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(readModel));
        await using var context = CreateContext();
        var consumer = new InventoryReservationRequestedConsumer(
            handler.Object,
            context,
            new IntegrationEventOutboxMessageFactory(),
            new FakeDateTimeProvider());

        var @event = new InventoryReservationRequestedIntegrationEvent(orderId, "SKU-1", 2, ExpiresAt)
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
                    && command.ExpiresAt == ExpiresAt),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulReservation_StagesReservedResultEvent_WithMetadata()
    {
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var readModel = CreateReadModel(orderId, "SKU-1", ExpiresAt);
        var handler = new Mock<ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReserveInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(readModel));
        await using var context = CreateContext();
        var consumer = new InventoryReservationRequestedConsumer(
            handler.Object,
            context,
            new IntegrationEventOutboxMessageFactory(),
            new FakeDateTimeProvider());

        var @event = new InventoryReservationRequestedIntegrationEvent(orderId, "SKU-1", 1, ExpiresAt)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = correlationId
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReservationRequestedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);

        var outbox = Assert.Single(TrackedOutboxMessages(context));
        Assert.Equal(InventoryReservedIntegrationEvent.EventType, outbox.Type);
        Assert.Equal(correlationId, outbox.CorrelationId);
        Assert.Equal(@event.EventId, outbox.CausationId);
        Assert.Equal(OutboxStatus.Pending, outbox.Status);
        Assert.NotEqual(Guid.Empty, outbox.EventId);

        var payload = JsonSerializer.Deserialize<InventoryReservedIntegrationEvent>(outbox.Payload);
        Assert.NotNull(payload);
        Assert.Equal(readModel.ReservationId, payload!.ReservationId);
        Assert.Equal(orderId, payload.OrderId);
        Assert.Equal("SKU-1", payload.Sku);
        Assert.Equal(readModel.Quantity, payload.Quantity);
        Assert.Equal(ExpiresAt, payload.ExpiresAt);
        Assert.Equal(InventoryReservationStatus.Pending, payload.Status);
        Assert.Equal(readModel.CreatedAtUtc, payload.CreatedAtUtc);
        Assert.Equal(readModel.UpdatedAtUtc, payload.UpdatedAtUtc);
        Assert.Equal(readModel.Version, payload.ReservationVersion);
        Assert.Equal(correlationId, payload.CorrelationId);
        Assert.Equal(@event.EventId, payload.CausationId);
    }

    [Fact]
    public async Task HandleAsync_FailedReservation_StagesFailedResultEvent_AndDoesNotThrow()
    {
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var handler = new Mock<ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReserveInventoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<InventoryReservationReadModel>(
                Error.Conflict("Inventory.InsufficientStock", "Not enough stock available.")));
        await using var context = CreateContext();
        var consumer = new InventoryReservationRequestedConsumer(
            handler.Object,
            context,
            new IntegrationEventOutboxMessageFactory(),
            new FakeDateTimeProvider());

        var @event = new InventoryReservationRequestedIntegrationEvent(orderId, "SKU-1", 99, ExpiresAt)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = correlationId
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReservationRequestedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);

        var outbox = Assert.Single(TrackedOutboxMessages(context));
        Assert.Equal(InventoryReservationFailedIntegrationEvent.EventType, outbox.Type);
        Assert.Equal(correlationId, outbox.CorrelationId);
        Assert.Equal(@event.EventId, outbox.CausationId);
        Assert.Equal(OutboxStatus.Pending, outbox.Status);

        var payload = JsonSerializer.Deserialize<InventoryReservationFailedIntegrationEvent>(outbox.Payload);
        Assert.NotNull(payload);
        Assert.Equal(orderId, payload!.OrderId);
        Assert.Equal("SKU-1", payload.Sku);
        Assert.Equal(99, payload.Quantity);
        Assert.Equal("Inventory.InsufficientStock", payload.Reason);
        Assert.Equal(correlationId, payload.CorrelationId);
        Assert.Equal(@event.EventId, payload.CausationId);
    }

    private static IReadOnlyList<OutboxMessage> TrackedOutboxMessages(InventoryDbContext context) =>
        context.ChangeTracker
            .Entries<OutboxMessage>()
            .Select(entry => entry.Entity)
            .ToList();

    private InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new InventoryDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        context.Database.EnsureCreated();

        return context;
    }

    private static InventoryReservationReadModel CreateReadModel(Guid orderId, string sku, DateTimeOffset expiresAt) =>
        new(
            Guid.NewGuid(),
            orderId,
            sku,
            1,
            FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus.Pending,
            expiresAt,
            new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
            InventoryReservation.InitialVersion);

    public void Dispose() => _connection.Dispose();

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}