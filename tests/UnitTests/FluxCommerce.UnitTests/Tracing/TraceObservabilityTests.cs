using System.Diagnostics;
using System.Text.Json;
using FluxCommerce.BuildingBlocks.Infrastructure.Messaging;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using FluxCommerce.UnitTests.Sagas;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using OrderSaga = FluxCommerce.Modules.Orders.Domain.Sagas.OrderSaga;
using OrderSagaTransitionOutcome = FluxCommerce.Modules.Orders.Domain.Enums.OrderSagaTransitionOutcome;

namespace FluxCommerce.UnitTests.Tracing;

/// <summary>
/// Phase 8.9: verifies the distributed-trace plumbing — the W3C trace context is captured when outbox
/// records are staged, injected into the transport headers by the publisher, restored as the parent of the
/// outbox dispatch producer span, extracted by the consumer processor, and used to tag the saga activities
/// with the workflow identifiers and previous/new saga state.
/// </summary>
public class TraceObservabilityTests : OrderSagaTestBase
{
    static TraceObservabilityTests()
    {
        // Phase 8.9 tests create raw Activity instances to simulate the producing/consuming pipeline;
        // force the W3C id format so traceparent values are W3C strings (the production code always
        // uses ActivitySource-created activities, which are W3C regardless).
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
    }

    [Fact]
    public void BuildProperties_WithCurrentActivity_AddsW3CTraceHeaders()
    {
        using var listener = new TestActivityListener();
        using var activity = new Activity("publisher.test");
        activity.TraceStateString = "vendors=test-value";
        activity.Start();

        var @event = CreateOrderCreated();
        var properties = RabbitMQPublisher.BuildProperties(OrderCreatedIntegrationEvent.EventType, @event);

        Assert.Equal(activity.Id, properties.Headers![TraceContext.TraceParentHeader]);
        Assert.Equal(activity.TraceStateString, properties.Headers[TraceContext.TraceStateHeader]);
        Assert.Equal(@event.EventId.ToString(), properties.Headers["EventId"]);
        Assert.Equal(@event.CorrelationId.ToString(), properties.Headers["CorrelationId"]);
    }

    [Fact]
    public void BuildProperties_WithoutCurrentActivity_OmitsTraceHeaders()
    {
        using var listener = new TestActivityListener();
        Assert.Null(Activity.Current);

        var @event = CreateOrderCreated();
        var properties = RabbitMQPublisher.BuildProperties(OrderCreatedIntegrationEvent.EventType, @event);

        Assert.False(properties.Headers!.ContainsKey(TraceContext.TraceParentHeader));
        Assert.False(properties.Headers.ContainsKey(TraceContext.TraceStateHeader));
    }

    [Fact]
    public void OutboxFactory_CapturesTraceContext_FromCurrentActivity()
    {
        using var listener = new TestActivityListener();
        using var activity = new Activity("stage.test");
        activity.Start();

        var factory = new IntegrationEventOutboxMessageFactory();
        var message = factory.Create(InventoryReservationRequestedIntegrationEvent.EventType, CreateReservationRequested());

        Assert.Equal(activity.Id, message.TraceParent);
        Assert.Equal(activity.TraceStateString, message.TraceState);
    }

    [Fact]
    public void OutboxFactory_WithoutCurrentActivity_HasNullTraceContext()
    {
        using var listener = new TestActivityListener();
        Assert.Null(Activity.Current);

        var factory = new IntegrationEventOutboxMessageFactory();
        var message = factory.Create(InventoryReservationRequestedIntegrationEvent.EventType, CreateReservationRequested());

        Assert.Null(message.TraceParent);
        Assert.Null(message.TraceState);
    }

    [Fact]
    public async Task OutboxDispatcher_StartsProducerSpan_RootedAtPersistedTraceParent()
    {
        using var listener = new TestActivityListener();

        using var root = new Activity("dispatch.root");
        root.Start();
        var rootTraceId = root.TraceId;

        var @event = CreateReservationRequested();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Type = InventoryReservationRequestedIntegrationEvent.EventType,
            Payload = JsonSerializer.Serialize((object)@event),
            CorrelationId = @event.CorrelationId,
            CausationId = @event.CausationId,
            OccurredAtUtc = @event.OccurredAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            Status = OutboxStatus.InProgress,
            RetryCount = 0,
            TraceParent = root.Id,
            TraceState = root.TraceStateString
        };

        var store = new Mock<IOutboxStore>();
        store.Setup(s => s.RecoverExpiredClaimsAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        store.Setup(s => s.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { message });
        store.Setup(s => s.MarkPublishedAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        string? activityAtPublish = null;
        var publisher = new Mock<IRabbitMQPublisher>();
        publisher.Setup(p => p.PublishAsync(message.Type, @event, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => activityAtPublish = Activity.Current?.Id);

        var dispatcher = new OrdersOutboxDispatcher(
            new Mock<IServiceScopeFactory>().Object,
            Options.Create(new OutboxDispatchOptions()),
            new CapturingLogger<OrdersOutboxDispatcher>());

        await dispatcher.DispatchCoreAsync(store.Object, publisher.Object, CancellationToken.None);

        publisher.Verify(p => p.PublishAsync(message.Type, @event, It.IsAny<CancellationToken>()), Times.Once);

        var dispatchSpan = Assert.Single(
            listener.Activities,
            activity => activity.OperationName == "Outbox.Dispatch" && activity.TraceId == rootTraceId);

        Assert.Equal(rootTraceId, dispatchSpan.TraceId);
        Assert.Equal(root.SpanId, dispatchSpan.ParentSpanId);
        Assert.Equal(activityAtPublish, dispatchSpan.Id);
        Assert.Equal(message.Id, dispatchSpan.GetTagItem("outbox.message.id"));
        Assert.Equal(message.Type, dispatchSpan.GetTagItem("event.type"));
        Assert.Equal(message.EventId, dispatchSpan.GetTagItem("event.id"));
        Assert.Equal(message.CorrelationId, dispatchSpan.GetTagItem("correlation.id"));
    }

    [Fact]
    public async Task StartFromOrderCreated_StartsSagaSpan_WithStateAndTraceTags()
    {
        using var listener = new TestActivityListener();
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var logger = new CapturingLogger<OrderSagaOrchestrator>();
        var orchestrator = CreateOrchestrator(context, logger: logger);

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        ActivityTraceId rootTraceId;
        ActivitySpanId rootSpanId;
        using (var root = new Activity("saga.start.test"))
        {
            root.Start();
            rootTraceId = root.TraceId;
            rootSpanId = root.SpanId;
            await orchestrator.StartFromOrderCreatedAsync(order.Id, correlationId, causationId, PlacedAtUtc);
        }

        await context.SaveChangesAsync();

        var saga = Assert.Single(context.OrderSagas.AsNoTracking().ToList());
        var startSpan = Assert.Single(
            listener.Activities,
            activity => activity.OperationName == "Orders.Saga.Start" && activity.TraceId == rootTraceId);

        Assert.Equal(rootTraceId, startSpan.TraceId);
        Assert.Equal(rootSpanId, startSpan.ParentSpanId);
        Assert.Equal(order.Id.Value, startSpan.GetTagItem("order.id"));
        Assert.Equal(saga.Id, startSpan.GetTagItem("saga.id"));
        Assert.Equal(OrderSagaState.Started.ToString(), startSpan.GetTagItem("saga.previous.state"));
        Assert.Equal(OrderSagaState.WaitingForInventory.ToString(), startSpan.GetTagItem("saga.new.state"));
        Assert.Equal(correlationId, startSpan.GetTagItem("correlation.id"));
        Assert.Equal(causationId, startSpan.GetTagItem("causation.id"));

        var createdLog = logger.Entries.SingleOrDefault(entry => entry.Message.Contains("Created saga"));
        Assert.NotNull(createdLog);
        Assert.Equal(OrderSagaState.Started.ToString(), createdLog["PreviousState"]?.ToString());
        Assert.Equal(OrderSagaState.WaitingForInventory.ToString(), createdLog["NewState"]?.ToString());
        Assert.Equal(rootTraceId.ToString(), createdLog["TraceId"]);
        Assert.NotNull(createdLog["SpanId"]);
    }

    [Fact]
    public async Task ApplyReservationSuccess_StartsSagaSpan_WithTransitionTags()
    {
        using var listener = new TestActivityListener();
        await using var context = CreateContext();
        var order = await CreatePlacedOrderAsync(("SKU-1", 1));
        var orchestrator = CreateOrchestrator(context);

        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        await orchestrator.StartFromOrderCreatedAsync(order.Id, correlationId, Guid.NewGuid(), PlacedAtUtc);
        await context.SaveChangesAsync();

        ActivityTraceId rootTraceId;
        using (var root = new Activity("saga.complete.test"))
        {
            root.Start();
            rootTraceId = root.TraceId;
            await orchestrator.ApplyReservationSuccessAsync(
                order.Id, reservationId, correlationId, causationId, PlacedAtUtc);
        }

        var successSpan = Assert.Single(
            listener.Activities,
            activity => activity.OperationName == "Orders.Saga.ApplyReservationSuccess" && activity.TraceId == rootTraceId);

        Assert.Equal(rootTraceId, successSpan.TraceId);
        Assert.Equal(OrderSagaState.WaitingForInventory.ToString(), successSpan.GetTagItem("saga.previous.state"));
        Assert.Equal(OrderSagaState.WaitingForPayment.ToString(), successSpan.GetTagItem("saga.new.state"));
        Assert.Equal(OrderSagaTransitionOutcome.Applied.ToString(), successSpan.GetTagItem("saga.outcome"));
        Assert.Equal(reservationId, successSpan.GetTagItem("reservation.id"));
    }

    private static OrderCreatedIntegrationEvent CreateOrderCreated() =>
        new(Guid.NewGuid(), Guid.NewGuid(), $"ORD-TRACE-{Guid.NewGuid():N}"[..16], DateTimeOffset.UtcNow)
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };

    private static InventoryReservationRequestedIntegrationEvent CreateReservationRequested() =>
        new(Guid.NewGuid(), "SKU-TRACE", 2, DateTimeOffset.UtcNow.AddMinutes(30))
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow
        };
}