using System.Text.Json;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Sagas;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 8.7: the compensation release leg (plan §12). When an order is cancelled after its
/// reservations were applied, <see cref="OrderSagaOrchestrator.RequestReleaseAsync"/> issues one
/// <c>inventory.release_requested</c> command per reservation recorded on the saga. A missing saga or a
/// saga with no recorded reservations is a benign no-op; the release commands are staged (never
/// committed by the orchestrator) and carry the workflow correlation/causation. Recording the
/// reservation ids happens when <c>inventory.reserved</c> is applied — on the transition AND on the
/// duplicate delivery of a later line-item result — and survives the JSON round-trip.
/// </summary>
public class OrderSagaReleaseTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset ResultAtUtc = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequestReleaseAsync_WithRecordedReservation_StagesOneReleaseCommand()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);
        var reservationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        await orchestrator.ApplyReservationSuccessAsync(
            order.Id, reservationId, correlationId, Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        var release = await orchestrator.RequestReleaseAsync(order.Id, correlationId, causationId);

        Assert.True(release.SagaFound);
        Assert.True(release.ReleaseIssued);
        var command = Assert.Single(release.Commands);
        Assert.Equal(reservationId, command.ReservationId);
        Assert.Equal(correlationId, command.CorrelationId);
        Assert.Equal(causationId, command.CausationId);
        Assert.NotEqual(Guid.Empty, command.EventId);

        var record = Assert.Single(TrackedReleaseOutboxMessages(context));
        Assert.Equal(OutboxStatus.Pending, record.Status);
        Assert.Equal(correlationId, record.CorrelationId);
        Assert.Equal(causationId, record.CausationId);

        var payload = JsonSerializer.Deserialize<InventoryReleaseRequestedIntegrationEvent>(record.Payload);
        Assert.NotNull(payload);
        Assert.Equal(reservationId, payload!.ReservationId);
    }

    [Fact]
    public async Task RequestReleaseAsync_IssuesOneCommandPerRecordedReservation()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2), ("SKU-2", 3));
        var orchestrator = await CreateWaitingSagaAsync(context, order);
        var firstReservationId = Guid.NewGuid();
        var secondReservationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // First line-item result transitions the saga; the second arrives on the already-completed saga
        // (duplicate delivery) and must still be recorded so both reservations are released.
        await orchestrator.ApplyReservationSuccessAsync(
            order.Id, firstReservationId, correlationId, Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        var duplicate = await orchestrator.ApplyReservationSuccessAsync(
            order.Id, secondReservationId, correlationId, Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);

        var release = await orchestrator.RequestReleaseAsync(order.Id, correlationId, Guid.NewGuid());

        Assert.True(release.ReleaseIssued);
        Assert.Equal(2, release.Commands.Count);
        Assert.Equal(
            new[] { firstReservationId, secondReservationId },
            release.Commands.Select(command => command.ReservationId));
        Assert.Equal(2, TrackedReleaseOutboxMessages(context).Count);
    }

    [Fact]
    public async Task RequestReleaseAsync_WhenSagaMissing_IsBenignNoOp()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = CreateOrchestrator(context);

        var release = await orchestrator.RequestReleaseAsync(order.Id, Guid.NewGuid(), Guid.NewGuid());

        Assert.False(release.SagaFound);
        Assert.False(release.ReleaseIssued);
        Assert.Null(release.SagaId);
        Assert.Empty(release.Commands);
        Assert.Equal(0, await context.OutboxMessages.CountAsync(
            message => message.Type == InventoryReleaseRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task RequestReleaseAsync_WhenNoReservationsRecorded_IsBenignNoOp()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var release = await orchestrator.RequestReleaseAsync(order.Id, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(release.SagaFound);
        Assert.False(release.ReleaseIssued);
        Assert.Empty(release.Commands);
        Assert.Equal(0, await context.OutboxMessages.CountAsync(
            message => message.Type == InventoryReleaseRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task RequestReleaseAsync_StagesCommands_OnlyAfterCommit()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);
        var reservationId = Guid.NewGuid();

        await orchestrator.ApplyReservationSuccessAsync(
            order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        await orchestrator.RequestReleaseAsync(order.Id, Guid.NewGuid(), Guid.NewGuid());

        // Nothing was committed: a fresh context must not see the staged release commands.
        await using var uncommittedContext = CreateContext();
        Assert.Equal(0, await uncommittedContext.OutboxMessages.CountAsync(
            message => message.Type == InventoryReleaseRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task ReservationIds_PersistAcrossContexts_AndRoundTripThroughJsonColumn()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);
        var reservationId = Guid.NewGuid();

        await orchestrator.ApplyReservationSuccessAsync(
            order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        await using var freshContext = CreateContext();
        var reloaded = await freshContext.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Completed, reloaded.State);
        Assert.Equal(reservationId, Assert.Single(reloaded.ReservationIds));
    }

    [Fact]
    public async Task RecordReservation_DuplicateId_IsIdempotent()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);
        var reservationId = Guid.NewGuid();

        await orchestrator.ApplyReservationSuccessAsync(
            order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        var saga = await context.OrderSagas.SingleAsync();
        var duplicate = saga.RecordReservation(reservationId);

        Assert.False(duplicate);
        Assert.Single(saga.ReservationIds);
    }

    private static IReadOnlyList<OutboxMessage> TrackedReleaseOutboxMessages(OrdersDbContext context) =>
        context.ChangeTracker
            .Entries<OutboxMessage>()
            .Where(entry => entry.Entity.Type == InventoryReleaseRequestedIntegrationEvent.EventType)
            .Select(entry => entry.Entity)
            .ToList();

    private async Task<OrderSagaOrchestrator> CreateWaitingSagaAsync(OrdersDbContext context, Order order)
    {
        var orchestrator = CreateOrchestrator(context);
        var start = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);

        Assert.True(start.SagaCreated);
        await context.SaveChangesAsync();
        Assert.Equal(OrderSagaState.WaitingForInventory, (await context.OrderSagas.SingleAsync()).State);

        return orchestrator;
    }
}