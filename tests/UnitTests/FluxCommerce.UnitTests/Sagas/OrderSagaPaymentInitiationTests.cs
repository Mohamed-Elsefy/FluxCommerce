using System.Text.Json;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Sagas;

/// <summary>
/// Phase 10.3: verifies the Order Saga payment initiation leg.
/// InventoryReserved → WaitingForPayment and stages payments.create_payment_requested
/// with authoritative amount/currency/customer and correlation/causation.
/// Idempotency: duplicate inventory.reserved on WaitingForPayment does not stage a second request.
/// </summary>
public class OrderSagaPaymentInitiationTests : OrderSagaTestBase
{
    private static readonly DateTimeOffset ResultAtUtc = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyReservationSuccess_StagesPaymentRequest_WhenTransitioningToWaitingForPayment()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 2));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var reservationId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var result = await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, correlationId, causationId, ResultAtUtc);
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Applied, result.Outcome);
        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Contains(reservationId, saga.ReservationIds);

        await using var freshContext = CreateContext();
        var message = await freshContext.OutboxMessages.SingleAsync(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType);
        Assert.Equal(correlationId, message.CorrelationId);
        Assert.Equal(causationId, message.CausationId);
        var payload = JsonSerializer.Deserialize<CreatePaymentRequestedIntegrationEvent>(message.Payload);
        Assert.NotNull(payload);
        Assert.Equal(order.Id.Value, payload!.OrderId);
        Assert.Equal(order.CustomerId.Value, payload.CustomerId);
        Assert.Equal(order.GrandTotal.Amount, payload.Amount);
        Assert.Equal(order.GrandTotal.Currency, payload.Currency);
    }

    [Fact]
    public async Task ApplyReservationSuccess_OnDuplicateInWaitingForPayment_DoesNotStageSecondPaymentRequest()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var correlationId = Guid.NewGuid();
        var firstReservation = Guid.NewGuid();
        var secondReservation = Guid.NewGuid();

        await orchestrator.ApplyReservationSuccessAsync(order.Id, firstReservation, correlationId, Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        var duplicate = await orchestrator.ApplyReservationSuccessAsync(order.Id, secondReservation, correlationId, Guid.NewGuid(), ResultAtUtc);
        await context.SaveChangesAsync();

        Assert.Equal(OrderSagaTransitionOutcome.Duplicate, duplicate.Outcome);
        var saga = await context.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.WaitingForPayment, saga.State);
        Assert.Equal(2, saga.ReservationIds.Count);

        await using var freshContext = CreateContext();
        var paymentMessages = await freshContext.OutboxMessages
            .Where(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType)
            .ToListAsync();
        Assert.Single(paymentMessages);
    }

    [Fact]
    public async Task ApplyReservationSuccess_AppliedPath_StillAtomic_BeforeCommitNotVisible()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ResultAtUtc);

        await using var uncommitted = CreateContext();
        Assert.Equal(OrderSagaState.WaitingForInventory, (await uncommitted.OrderSagas.SingleAsync()).State);
        Assert.Empty(await uncommitted.OutboxMessages.Where(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType).ToListAsync());
    }

    [Fact]
    public async Task ApplyReservationSuccess_CorrelationPropagation()
    {
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = await CreateWaitingSagaAsync(context, order);

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), correlationId, causationId, ResultAtUtc);
        await context.SaveChangesAsync();

        await using var fresh = CreateContext();
        var msg = await fresh.OutboxMessages.SingleAsync(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType);
        Assert.Equal(correlationId, msg.CorrelationId);
        Assert.Equal(causationId, msg.CausationId);
        var payload = JsonSerializer.Deserialize<CreatePaymentRequestedIntegrationEvent>(msg.Payload);
        Assert.Equal(correlationId, payload!.CorrelationId);
        Assert.Equal(causationId, payload.CausationId);
    }

    private async Task<OrderSagaOrchestrator> CreateWaitingSagaAsync(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext context, FluxCommerce.Modules.Orders.Domain.Aggregates.Order order)
    {
        var orchestrator = CreateOrchestrator(context);
        var start = await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        Assert.True(start.SagaCreated);
        await context.SaveChangesAsync();
        Assert.Equal(OrderSagaState.WaitingForInventory, (await context.OrderSagas.SingleAsync()).State);
        return orchestrator;
    }
}
