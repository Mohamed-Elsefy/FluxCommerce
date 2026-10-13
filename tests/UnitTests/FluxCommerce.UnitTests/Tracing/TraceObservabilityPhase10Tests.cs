using System.Diagnostics;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.Modules.Payments.Contracts.IntegrationEvents;
using FluxCommerce.UnitTests.Sagas;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FluxCommerce.UnitTests.Tracing;

/// <summary>
/// Phase 10.11: verifies distributed tracing and structured logging for the full
/// Checkout → OrderCreated → ReserveInventory → InventoryReserved → CreatePayment →
/// PaymentSucceeded/Failed → Compensation/Refund → OrderCancelled flow.
/// Ensures CorrelationId/CausationId/TraceId/SpanId propagate via outbox and RabbitMQ,
/// and that saga transition logs capture OrderId/SagaId/PaymentId/ReservationId/EventId/CorrelationId etc.
/// </summary>
public class TraceObservabilityPhase10Tests : OrderSagaTestBase
{
    static TraceObservabilityPhase10Tests()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
    }

    [Fact]
    public async Task PaymentInitiation_PreservesCorrelationAndTraceAcrossOutbox()
    {
        using var listener = new TestActivityListener();
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        ActivityTraceId traceId;
        using (var root = new Activity("payment.initiation.test"))
        {
            root.Start();
            traceId = root.TraceId;
            await orchestrator.ApplyReservationSuccessAsync(order.Id, reservationId, correlationId, causationId, PlacedAtUtc.AddMinutes(1));
        }
        await ctx.SaveChangesAsync();

        var outbox = await ctx.OutboxMessages.FirstAsync(m => m.Type == CreatePaymentRequestedIntegrationEvent.EventType);
        Assert.Equal(correlationId, outbox.CorrelationId);
        Assert.Equal(causationId, outbox.CausationId);
        Assert.NotNull(outbox.TraceParent);
        Assert.Contains(traceId.ToString(), outbox.TraceParent!);

        var payload = JsonSerializer.Deserialize<CreatePaymentRequestedIntegrationEvent>(outbox.Payload);
        Assert.Equal(order.Id.Value, payload!.OrderId);
        Assert.Equal(correlationId, payload.CorrelationId);
        Assert.Equal(causationId, payload.CausationId);
    }

    [Fact]
    public async Task PaymentSucceeded_AppliesSagaWithTraceAndLogsIdentities()
    {
        using var listener = new TestActivityListener();
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();

        var paymentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var logger = new CapturingLogger<OrderSagaOrchestrator>();
        var orchestratorWithLogger = CreateOrchestrator(ctx, logger: logger);

        ActivityTraceId traceId;
        using (var root = new Activity("payment.succeeded.test"))
        {
            root.Start();
            traceId = root.TraceId;
            await orchestratorWithLogger.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test_123", correlationId, causationId, PlacedAtUtc.AddMinutes(2));
        }

        var saga = await ctx.OrderSagas.SingleAsync();
        var successSpan = Assert.Single(listener.Activities, a => a.OperationName == "Orders.Saga.ApplyPaymentSucceeded" && a.TraceId == traceId);
        Assert.Equal(order.Id.Value, successSpan.GetTagItem("order.id"));
        Assert.Equal(saga.Id, successSpan.GetTagItem("saga.id"));
        Assert.Equal(correlationId, successSpan.GetTagItem("correlation.id"));
        Assert.Equal(causationId, successSpan.GetTagItem("causation.id"));

        var log = logger.Entries.FirstOrDefault(e => e.Message.Contains("Applied payments.payment_succeeded"));
        Assert.NotNull(log);
        Assert.Equal(order.Id.Value.ToString(), log["OrderId"]?.ToString());
        Assert.Equal(saga.Id.ToString(), log["SagaId"]?.ToString());
        Assert.Equal(paymentId.ToString(), log["PaymentId"]?.ToString());
        Assert.Equal(correlationId.ToString(), log["CorrelationId"]?.ToString());
        Assert.Equal(causationId.ToString(), log["CausationId"]?.ToString());
        Assert.NotNull(log["TraceId"]);
        Assert.NotNull(log["SpanId"]);
        Assert.Equal("WaitingForPayment", log["PreviousState"]?.ToString());
        Assert.Equal("Completed", log["NewState"]?.ToString());
    }

    [Fact]
    public async Task PaymentFailed_TriggersCompensationWithTraceAndLogs()
    {
        using var listener = new TestActivityListener();
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();

        var paymentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var logger = new CapturingLogger<OrderSagaOrchestrator>();
        var orchestratorWithLogger = CreateOrchestrator(ctx, logger: logger);

        ActivityTraceId traceId;
        using (var root = new Activity("payment.failed.test"))
        {
            root.Start();
            traceId = root.TraceId;
            await orchestratorWithLogger.ApplyPaymentFailedAsync(order.Id, paymentId, "pi_test", "card_declined", correlationId, causationId, PlacedAtUtc.AddMinutes(2));
        }

        var span = Assert.Single(listener.Activities, a => a.OperationName == "Orders.Saga.ApplyPaymentFailed" && a.TraceId == traceId);
        Assert.Equal(correlationId, span.GetTagItem("correlation.id"));
        var log = logger.Entries.FirstOrDefault(e => e.Message.Contains("Applied payments.payment_failed"));
        Assert.NotNull(log);
        Assert.Equal(paymentId.ToString(), log["PaymentId"]?.ToString());
        Assert.Equal("card_declined", log["Reason"]?.ToString() ?? log["FailureReason"]?.ToString());
    }

    [Fact]
    public async Task RefundFlow_PreservesTraceAcrossSagaAndOutbox()
    {
        using var listener = new TestActivityListener();
        await using var ctx = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(ctx);
        await orchestrator.StartFromOrderCreatedAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc);
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyReservationSuccessAsync(order.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1));
        await ctx.SaveChangesAsync();
        var paymentId = Guid.NewGuid();
        await orchestrator.ApplyPaymentInitiatedAsync(order.Id, paymentId, "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(1).AddSeconds(30));
        await ctx.SaveChangesAsync();
        await orchestrator.ApplyPaymentSucceededAsync(order.Id, paymentId, "pi_test_123", Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(2));
        await ctx.SaveChangesAsync();

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        ActivityTraceId traceId;
        using (var root = new Activity("refund.test"))
        {
            root.Start();
            traceId = root.TraceId;
            await orchestrator.RequestRefundAsync(order.Id, "downstream failure", correlationId, causationId);
        }
        await ctx.SaveChangesAsync();

        var outbox = await ctx.OutboxMessages.FirstAsync(m => m.Type == RefundPaymentRequestedIntegrationEvent.EventType);
        Assert.Equal(correlationId, outbox.CorrelationId);
        Assert.Equal(causationId, outbox.CausationId);
        Assert.NotNull(outbox.TraceParent);
        Assert.Contains(traceId.ToString(), outbox.TraceParent!);

        var payload = JsonSerializer.Deserialize<RefundPaymentRequestedIntegrationEvent>(outbox.Payload);
        Assert.Equal(order.Id.Value, payload!.OrderId);
        Assert.Equal(correlationId, payload.CorrelationId);
    }

    [Fact]
    public async Task InventoryReleased_CompensationPreservesCorrelationAndLogs()
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
        await orchestrator.ApplyPaymentFailedAsync(order.Id, Guid.NewGuid(), "pi_test", "card_declined", Guid.NewGuid(), Guid.NewGuid(), PlacedAtUtc.AddMinutes(2));
        await ctx.SaveChangesAsync();

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var logger = new CapturingLogger<OrderSagaOrchestrator>();
        var orchestratorWithLogger = CreateOrchestrator(ctx, logger: logger);

        ActivityTraceId traceId;
        using (var root = new Activity("inventory.released.test"))
        {
            root.Start();
            traceId = root.TraceId;
            await orchestratorWithLogger.ApplyInventoryReleasedAsync(order.Id, reservationId, correlationId, causationId, PlacedAtUtc.AddMinutes(3));
        }

        var span = Assert.Single(listener.Activities, a => a.OperationName == "Orders.Saga.ApplyInventoryReleased" && a.TraceId == traceId);
        Assert.Equal(reservationId, span.GetTagItem("reservation.id"));
        var log = logger.Entries.FirstOrDefault(e => e.Message.Contains("Applied inventory.released"));
        Assert.NotNull(log);
        Assert.Equal(order.Id.Value.ToString(), log["OrderId"]?.ToString());
        Assert.Equal(reservationId.ToString(), log["ReservationId"]?.ToString());
        Assert.Equal(correlationId.ToString(), log["CorrelationId"]?.ToString());
        Assert.Equal(causationId.ToString(), log["CausationId"]?.ToString());
    }

    [Fact]
    public void OutboxFactory_CapturesTraceForRefundEvents()
    {
        using var listener = new TestActivityListener();
        using var activity = new Activity("refund.stage.test");
        activity.Start();

        var factory = new IntegrationEventOutboxMessageFactory();
        var refundEvent = new RefundPaymentRequestedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), "pi_test", 10m, "USD", "reason")
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
        var message = factory.Create(RefundPaymentRequestedIntegrationEvent.EventType, refundEvent);
        Assert.Equal(activity.Id, message.TraceParent);
        Assert.Equal(activity.TraceStateString, message.TraceState);
    }
}






