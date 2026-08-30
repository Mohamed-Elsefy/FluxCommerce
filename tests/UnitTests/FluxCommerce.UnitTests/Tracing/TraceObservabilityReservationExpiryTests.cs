using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Consuming;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.UnitTests.Sagas;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrderSagaTransitionOutcome = FluxCommerce.Modules.Orders.Infrastructure.Sagas.OrderSagaTransitionOutcome;

namespace FluxCommerce.UnitTests.Tracing;

/// <summary>
/// Phase 11.9 (Observability &amp; Distributed Tracing) for the reservation-expiration flow:
/// verifies that CorrelationId/CausationId/EventId propagate from the Inventory outbox through the
/// Orders consumer into the saga orchestrator, that the consumer/saga logs identify the workflow
/// (OrderId, SagaId, ReservationId, CorrelationId) plus the W3C TraceId/SpanId, and that the saga
/// transition activity is rooted in the consumer trace context (end-to-end span hierarchy).
/// </summary>
public class TraceObservabilityReservationExpiryTests : OrderSagaTestBase
{
    static TraceObservabilityReservationExpiryTests()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
    }

    [Fact]
    public async Task ReservationExpired_ConsumerAndSagaLogCorrelationIdentity_AndActivityIsRootedInConsumerTrace()
    {
        using var listener = new TestActivityListener();
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var consumerLogger = new CapturingLogger<InventoryReservationExpiredConsumer>();
        var sagaLogger = new CapturingLogger<OrderSagaOrchestrator>();

        ActivityTraceId traceId;
        ActivitySpanId parentSpanId;
        using (var root = new Activity("consumer.inventory.reservation_expired.test"))
        {
            root.Start();
            traceId = root.TraceId;
            parentSpanId = root.SpanId;

            var @event = new InventoryReservationExpiredIntegrationEvent(
                reservationId, order.Id.Value, "SKU-1", 1, PlacedAtUtc.AddMinutes(2))
            {
                EventId = eventId,
                CorrelationId = correlationId,
                CausationId = causationId,
                OccurredAtUtc = PlacedAtUtc.AddMinutes(2).UtcDateTime
            };
            var envelope = IntegrationEventEnvelope.From(@event, InventoryReservationExpiredIntegrationEvent.EventType);

            var consumer = new InventoryReservationExpiredConsumer(
                CreateOrchestrator(ctx, logger: sagaLogger),
                consumerLogger);
            await consumer.HandleAsync(@event, envelope);
        }
        await ctx.SaveChangesAsync();

        var saga = await ctx.OrderSagas.SingleAsync();
        Assert.Equal(OrderSagaState.Cancelled, saga.State);

        // Consumer "Expiry Consumer Received" log carries the event + correlation identity.
        var consumerLog = consumerLogger.Entries.FirstOrDefault(e => e.Message.Contains("expired at"));
        Assert.NotNull(consumerLog);
        Assert.Equal(order.Id.Value.ToString(), consumerLog["OrderId"]?.ToString());
        Assert.Equal(reservationId.ToString(), consumerLog["ReservationId"]?.ToString());
        Assert.Equal(eventId.ToString(), consumerLog["EventId"]?.ToString());
        Assert.Equal(correlationId.ToString(), consumerLog["CorrelationId"]?.ToString());
        Assert.Equal(causationId.ToString(), consumerLog["CausationId"]?.ToString());

        // Saga "Applied" log identifies Order/Saga/Reservation/Correlation and the W3C trace.
        var appliedLog = sagaLogger.Entries.FirstOrDefault(e => e.Message.Contains("Applied inventory.reservation_expired"));
        Assert.NotNull(appliedLog);
        Assert.Equal(order.Id.Value.ToString(), appliedLog["OrderId"]?.ToString());
        Assert.Equal(saga.Id.ToString(), appliedLog["SagaId"]?.ToString());
        Assert.Equal(reservationId.ToString(), appliedLog["ReservationId"]?.ToString());
        Assert.Equal(correlationId.ToString(), appliedLog["CorrelationId"]?.ToString());
        // The consumer forwards envelope.ProducedEventCausationId (== the envelope/event EventId) as the saga causation.
        Assert.Equal(eventId.ToString(), appliedLog["CausationId"]?.ToString());
        Assert.Equal("WaitingForPayment", appliedLog["PreviousState"]?.ToString());
        Assert.Equal("Cancelled", appliedLog["NewState"]?.ToString());
        Assert.Equal(OrderSagaTransitionOutcome.Applied.ToString(), appliedLog["Outcome"]?.ToString());
        Assert.Equal(traceId.ToString(), appliedLog["TraceId"]);
        Assert.NotNull(appliedLog["SpanId"]);

        // The saga span is rooted in the consumer trace and carries the workflow tags end-to-end.
        var sagaSpan = Assert.Single(
            listener.Activities,
            a => a.OperationName == "Orders.Saga.ApplyReservationExpired" && a.TraceId == traceId);
        Assert.Equal(traceId, sagaSpan.TraceId);
        Assert.Equal(parentSpanId, sagaSpan.ParentSpanId);
        Assert.Equal(order.Id.Value, sagaSpan.GetTagItem("order.id"));
        Assert.Equal(saga.Id, sagaSpan.GetTagItem("saga.id"));
        Assert.Equal(reservationId, sagaSpan.GetTagItem("reservation.id"));
        Assert.Equal(correlationId, sagaSpan.GetTagItem("correlation.id"));
        Assert.Equal(eventId, sagaSpan.GetTagItem("causation.id"));
        Assert.Equal(OrderSagaState.WaitingForPayment.ToString(), sagaSpan.GetTagItem("saga.previous.state"));
        Assert.Equal(OrderSagaState.Cancelled.ToString(), sagaSpan.GetTagItem("saga.new.state"));
        Assert.Equal(OrderSagaTransitionOutcome.Applied.ToString(), sagaSpan.GetTagItem("saga.outcome"));
    }

    [Fact]
    public async Task ReservationExpired_TerminalSaga_SafeIgnoreLogged_WithCorrelationAndTrace()
    {
        using var listener = new TestActivityListener();
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        var reservationId = Guid.NewGuid();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        // Cancel first so the saga is terminal when the (late) expiry arrives.
        await orchestrator.ApplyReservationExpiredAsync(order.Id, reservationId, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(2));
        await ctx.SaveChangesAsync();

        var correlationId = Guid.NewGuid();
        var sagaLogger = new CapturingLogger<OrderSagaOrchestrator>();

        ActivityTraceId traceId;
        using (var root = new Activity("consumer.expiry.late.test"))
        {
            root.Start();
            traceId = root.TraceId;
            await CreateOrchestrator(ctx, logger: sagaLogger)
                .ApplyReservationExpiredAsync(order.Id, reservationId, correlationId, Guid.NewGuid(), PlacedAtUtc.AddMinutes(3));
        }

        // Safe-ignore log is structured and identifies the saga/order/correlation with the trace.
        var ignoreLog = sagaLogger.Entries.FirstOrDefault(e => e.Message.Contains("safely ignored"));
        Assert.NotNull(ignoreLog);
        Assert.Equal(order.Id.Value.ToString(), ignoreLog["OrderId"]?.ToString());
        Assert.Equal(correlationId.ToString(), ignoreLog["CorrelationId"]?.ToString());
        Assert.Equal(OrderSagaTransitionOutcome.Ignored.ToString(), ignoreLog["Outcome"]?.ToString());
        Assert.Equal(traceId.ToString(), ignoreLog["TraceId"]);
        Assert.NotNull(ignoreLog["SpanId"]);
    }
}
