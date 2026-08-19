using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 8.3: the Order Saga orchestrator starts the <c>OrderCreated → Reserve Inventory</c> leg. These
/// tests verify the saga creation/advance, the one-command-per-line-item mapping, the Phase 7.8 trace
/// identifiers and deterministic ExpiresAt on the produced commands, and the idempotent no-op behavior
/// on any re-delivery (duplicate, already-completed, already-failed) — including repeated command
/// generation safety and the missing-order guard.
/// </summary>
public class OrderSagaOrchestratorTests : OrderSagaTestBase
{
    [Fact]
    public async Task StartFromOrderCreated_CreatesSaga_AndStagesCommands()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2), ("SKU-2", 1));
        var orchestrator = CreateOrchestrator(context);

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var result = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, correlationId, causationId, PlacedAtUtc);

        await context.SaveChangesAsync();

        Assert.True(result.SagaCreated);
        Assert.Equal(2, result.Commands.Count);

        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(order.Id, saga.OrderId);
        Assert.Equal(correlationId, saga.CorrelationId);
        Assert.Equal(OrderSagaState.WaitingForInventory, saga.State);
    }

    [Fact]
    public async Task StartFromOrderCreated_StagesOneCommandPerLineItem()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2), ("SKU-2", 1), ("SKU-3", 5));
        var orchestrator = CreateOrchestrator(context);

        var result = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);

        var commands = result.Commands.OrderBy(command => command.Sku).ToArray();

        Assert.Equal(3, commands.Length);
        Assert.Equal("SKU-1", commands[0].Sku);
        Assert.Equal(2, commands[0].Quantity);
        Assert.Equal("SKU-2", commands[1].Sku);
        Assert.Equal(1, commands[1].Quantity);
        Assert.Equal("SKU-3", commands[2].Sku);
        Assert.Equal(5, commands[2].Quantity);
    }

    [Fact]
    public async Task StartFromOrderCreated_StampsTraceIdentifiers_AndOccurredAtUtc()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var dateTimeProvider = new FakeDateTimeProvider { UtcNow = new DateTime(2026, 8, 19, 8, 20, 0, DateTimeKind.Utc) };
        var orchestrator = CreateOrchestrator(context, dateTimeProvider);

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var result = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, correlationId, causationId, PlacedAtUtc);

        var command = result.Commands.Single();

        Assert.Equal(order.Id.Value, command.OrderId);
        Assert.Equal(correlationId, command.CorrelationId);
        Assert.Equal(causationId, command.CausationId);
        Assert.Equal(dateTimeProvider.UtcNow, command.OccurredAtUtc);
    }

    [Fact]
    public async Task StartFromOrderCreated_ExpiresAt_IsPlaceTimePlusConfiguredLifetime()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(
            context,
            options: new ReservationRequestOptions { ReservationLifetime = TimeSpan.FromMinutes(45) });

        var result = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);

        Assert.Equal(PlacedAtUtc.AddMinutes(45), result.Commands.Single().ExpiresAt);
    }

    [Fact]
    public async Task StartFromOrderCreated_StagesOutboxMessages_ForEachCommand()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1), ("SKU-2", 3));
        var orchestrator = CreateOrchestrator(context);

        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);

        await context.SaveChangesAsync();

        var messages = await context.OutboxMessages
            .Where(message => message.Type == InventoryReservationRequestedIntegrationEvent.EventType)
            .ToListAsync();

        Assert.Equal(2, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.Equal(FluxCommerce.BuildingBlocks.Outbox.Messages.OutboxStatus.Pending, message.Status);
            Assert.Equal(0, message.RetryCount);
        });
    }

    [Fact]
    public async Task StartFromOrderCreated_OnDuplicateDelivery_IsNoOp()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(context);

        var correlationId = Guid.NewGuid();
        var first = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, correlationId, Guid.NewGuid(), PlacedAtUtc);

        await context.SaveChangesAsync();

        var second = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, correlationId, Guid.NewGuid(), PlacedAtUtc);

        Assert.True(first.SagaCreated);
        Assert.False(second.SagaCreated);
        Assert.Empty(second.Commands);
        Assert.Single(await context.OrderSagas.ToListAsync());
        Assert.Equal(
            1,
            await context.OutboxMessages.CountAsync(
                message => message.Type == InventoryReservationRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task StartFromOrderCreated_OnCompletedSaga_IsNoOp()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(context);

        var correlationId = Guid.NewGuid();
        var first = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, correlationId, Guid.NewGuid(), PlacedAtUtc);

        await context.SaveChangesAsync();

        var saga = await context.OrderSagas.SingleAsync();
        saga.Complete(PlacedAtUtc);
        await context.SaveChangesAsync();

        var second = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, correlationId, Guid.NewGuid(), PlacedAtUtc);

        Assert.False(second.SagaCreated);
        Assert.Empty(second.Commands);
        Assert.Equal(OrderSagaState.Completed, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(
            1,
            await context.OutboxMessages.CountAsync(
                message => message.Type == InventoryReservationRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task StartFromOrderCreated_OnFailedSaga_IsNoOp()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(context);

        var correlationId = Guid.NewGuid();
        var first = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, correlationId, Guid.NewGuid(), PlacedAtUtc);

        await context.SaveChangesAsync();

        var saga = await context.OrderSagas.SingleAsync();
        saga.Fail(PlacedAtUtc);
        await context.SaveChangesAsync();

        var second = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, correlationId, Guid.NewGuid(), PlacedAtUtc);

        Assert.False(second.SagaCreated);
        Assert.Empty(second.Commands);
        Assert.Equal(OrderSagaState.Failed, (await context.OrderSagas.SingleAsync()).State);
        Assert.Equal(
            1,
            await context.OutboxMessages.CountAsync(
                message => message.Type == InventoryReservationRequestedIntegrationEvent.EventType));
    }

    [Fact]
    public async Task StartFromOrderCreated_WhenOrderMissing_Throws()
    {
        await using var context = CreateContext();
        var orchestrator = CreateOrchestrator(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.StartFromOrderCreatedAsync(OrderId.Create(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc));

        Assert.Contains("does not exist", exception.Message);
        Assert.Empty(await context.OrderSagas.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task StartFromOrderCreated_CommitsSagaAndCommands_Atomically()
    {
        // The orchestrator only stages state; the commit is owned by the caller. Before the commit the
        // saga and outbox records must not be visible to a fresh context (nothing was persisted).
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(context);

        var result = await orchestrator.StartFromOrderCreatedAsync(
            order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);

        Assert.True(result.SagaCreated);

        await using var uncommittedContext = CreateContext();
        Assert.False(await uncommittedContext.OrderSagas.AnyAsync());
        Assert.False(await uncommittedContext.OutboxMessages.AnyAsync(
            message => message.Type == InventoryReservationRequestedIntegrationEvent.EventType));
    }
}