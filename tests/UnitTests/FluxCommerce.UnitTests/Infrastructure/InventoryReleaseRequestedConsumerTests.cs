using System.Text.Json;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Application.Commands.ReleaseReservation;
using FluxCommerce.Modules.Inventory.Application.Errors;
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
using DomainReservationStatus = FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus;

namespace FluxCommerce.UnitTests.Infrastructure;

/// <summary>
/// Phase 8.7: the Inventory release-request consumer is a thin adapter that maps each saga-issued
/// <c>inventory.release_requested</c> event to the <see cref="ReleaseReservationCommand"/> use case,
/// delegates the business operation to the Inventory application handler, and stages the
/// <c>inventory.released</c> result event on success — carrying the workflow correlation and the causal
/// link back to the consumed command. Release idempotency: a repeated release of an already-terminal
/// reservation (<c>Inventory.InvalidStateTransition</c>) is a benign no-op that acknowledges the
/// delivery without side effects; any other failure (e.g. a missing reservation) is an anomaly that
/// throws, so the infrastructure retries then dead-letters the delivery — a release is never silently
/// lost.
/// </summary>
public class InventoryReleaseRequestedConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public InventoryReleaseRequestedConsumerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public void HandlerName_IsStableAndUnique()
    {
        Assert.Equal("Inventory.ReleaseReservation", InventoryReleaseRequestedConsumer.StableHandlerName);
    }

    [Fact]
    public async Task HandleAsync_MapsEventToReleaseCommand_AndInvokesUseCase()
    {
        var reservationId = Guid.NewGuid();
        var readModel = CreateReadModel(reservationId, "SKU-1", DomainReservationStatus.Released);
        var handler = new Mock<ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReleaseReservationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(readModel));
        await using var context = CreateContext();
        var consumer = new InventoryReleaseRequestedConsumer(
            handler.Object,
            context,
            new IntegrationEventOutboxMessageFactory(),
            new FakeDateTimeProvider());

        var @event = new InventoryReleaseRequestedIntegrationEvent(reservationId)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReleaseRequestedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);

        handler.Verify(h => h.HandleAsync(It.IsAny<ReleaseReservationCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        handler.Verify(
            h => h.HandleAsync(
                It.Is<ReleaseReservationCommand>(command => command.ReservationId == reservationId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulRelease_StagesReleasedEvent_WithMetadata()
    {
        var reservationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var readModel = CreateReadModel(reservationId, "SKU-1", DomainReservationStatus.Released);
        var handler = new Mock<ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReleaseReservationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(readModel));
        await using var context = CreateContext();
        var consumer = new InventoryReleaseRequestedConsumer(
            handler.Object,
            context,
            new IntegrationEventOutboxMessageFactory(),
            new FakeDateTimeProvider());

        var @event = new InventoryReleaseRequestedIntegrationEvent(reservationId)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = correlationId
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReleaseRequestedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);

        var outbox = Assert.Single(TrackedOutboxMessages(context));
        Assert.Equal(InventoryReleasedIntegrationEvent.EventType, outbox.Type);
        Assert.Equal(correlationId, outbox.CorrelationId);
        Assert.Equal(@event.EventId, outbox.CausationId);
        Assert.Equal(OutboxStatus.Pending, outbox.Status);
        Assert.NotEqual(Guid.Empty, outbox.EventId);

        var payload = JsonSerializer.Deserialize<InventoryReleasedIntegrationEvent>(outbox.Payload);
        Assert.NotNull(payload);
        Assert.Equal(reservationId, payload!.ReservationId);
        Assert.Equal(readModel.OrderId, payload.OrderId);
        Assert.Equal("SKU-1", payload.Sku);
        Assert.Equal(readModel.Quantity, payload.Quantity);
        Assert.Equal(readModel.ExpiresAt, payload.ExpiresAt);
        Assert.Equal(InventoryReservationStatus.Released, payload.Status);
        Assert.Equal(readModel.CreatedAtUtc, payload.CreatedAtUtc);
        Assert.Equal(readModel.UpdatedAtUtc, payload.UpdatedAtUtc);
        Assert.Equal(readModel.Version, payload.ReservationVersion);
        Assert.Equal(correlationId, payload.CorrelationId);
        Assert.Equal(@event.EventId, payload.CausationId);
    }

    [Fact]
    public async Task HandleAsync_AlreadyTerminalReservation_IsBenignNoOp_AndDoesNotThrow()
    {
        var reservationId = Guid.NewGuid();
        var handler = new Mock<ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReleaseReservationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<InventoryReservationReadModel>(
                Error.Conflict(InventoryReservationErrors.InvalidStateTransition.Code, "Already released.")));
        await using var context = CreateContext();
        var consumer = new InventoryReleaseRequestedConsumer(
            handler.Object,
            context,
            new IntegrationEventOutboxMessageFactory(),
            new FakeDateTimeProvider());

        var @event = new InventoryReleaseRequestedIntegrationEvent(reservationId)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReleaseRequestedIntegrationEvent.EventType);

        await consumer.HandleAsync(@event, envelope);

        Assert.Empty(TrackedOutboxMessages(context));
    }

    [Fact]
    public async Task HandleAsync_MissingReservation_Throws_SoDeliveryIsRetriedAndDeadLettered()
    {
        var reservationId = Guid.NewGuid();
        var handler = new Mock<ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReleaseReservationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<InventoryReservationReadModel>(InventoryReservationErrors.ReservationNotFound));
        await using var context = CreateContext();
        var consumer = new InventoryReleaseRequestedConsumer(
            handler.Object,
            context,
            new IntegrationEventOutboxMessageFactory(),
            new FakeDateTimeProvider());

        var @event = new InventoryReleaseRequestedIntegrationEvent(reservationId)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReleaseRequestedIntegrationEvent.EventType);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(@event, envelope));

        Assert.Contains(reservationId.ToString(), exception.Message);
        Assert.Empty(TrackedOutboxMessages(context));
    }

    [Fact]
    public async Task HandleAsync_OtherFailure_Throws_SoDeliveryIsRetriedAndDeadLettered()
    {
        var reservationId = Guid.NewGuid();
        var handler = new Mock<ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<ReleaseReservationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<InventoryReservationReadModel>(
                Error.Failure("Inventory.ReservationLifecycleFailed", "The reservation operation failed.")));
        await using var context = CreateContext();
        var consumer = new InventoryReleaseRequestedConsumer(
            handler.Object,
            context,
            new IntegrationEventOutboxMessageFactory(),
            new FakeDateTimeProvider());

        var @event = new InventoryReleaseRequestedIntegrationEvent(reservationId)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        };
        var envelope = IntegrationEventEnvelope.From(@event, InventoryReleaseRequestedIntegrationEvent.EventType);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(@event, envelope));

        Assert.Contains("Inventory.ReservationLifecycleFailed", exception.Message);
        Assert.Empty(TrackedOutboxMessages(context));
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

    private static InventoryReservationReadModel CreateReadModel(Guid reservationId, string sku, DomainReservationStatus status) =>
        new(
            reservationId,
            Guid.NewGuid(),
            sku,
            1,
            status,
            new DateTimeOffset(2026, 8, 19, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 19, 10, 15, 0, TimeSpan.Zero),
            InventoryReservation.InitialVersion + 1);

    public void Dispose() => _connection.Dispose();

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}