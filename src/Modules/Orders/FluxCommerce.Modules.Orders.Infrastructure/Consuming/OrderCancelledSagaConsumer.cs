using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
using Microsoft.Extensions.Logging;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 8.7: the Orders consumer for the <c>orders.order_cancelled</c> event. It is a thin
/// transport-agnostic adapter: it reads the order id and the Phase 7.8 trace identifiers from the
/// contract and envelope, validates the payload, and delegates the whole compensation release to
/// <see cref="OrderSagaOrchestrator.RequestReleaseAsync"/>. The staged release commands commit atomically
/// with the Orders idempotent-processing boundary (<c>ProcessedMessage</c> record), so a redelivered
/// cancellation is acknowledged without re-execution and the release is never issued twice. When the
/// workflow never reserved inventory for the order (e.g. the 8.6 failure path already cancelled it) the
/// orchestrator is a benign no-op. Its stable <see cref="HandlerName"/> (<c>Orders.RequestInventoryRelease</c>)
/// scopes the idempotency identity (<c>EventId + HandlerName</c>). The consumer never touches RabbitMQ,
/// messaging transport types, or the DbContext directly.
/// </summary>
public sealed class OrderCancelledSagaConsumer : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public const string StableHandlerName = "Orders.RequestInventoryRelease";

    private readonly OrderSagaOrchestrator _orchestrator;
    private readonly ILogger<OrderCancelledSagaConsumer> _logger;

    public OrderCancelledSagaConsumer(
        OrderSagaOrchestrator orchestrator,
        ILogger<OrderCancelledSagaConsumer> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        OrderCancelledIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        Validate(@event);

        _logger.LogInformation(
            "Order {OrderId} was cancelled; requesting the inventory release for its recorded reservations.",
            @event.OrderId);

        return _orchestrator.RequestReleaseAsync(
            new OrderId(@event.OrderId),
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            cancellationToken);
    }

    private static void Validate(OrderCancelledIntegrationEvent @event)
    {
        if (@event.OrderId == Guid.Empty)
            throw new ArgumentException("An order id is required.", nameof(@event));
    }
}