using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// The single registered consumer of <c>orders.order_created</c> on the
/// <c>q.orders.summary</c> queue. The consumer infrastructure resolves exactly one
/// handler per event type, so the summary projection and the saga boot share this
/// adapter and execute atomically inside the same Orders idempotent-processing
/// boundary (projection row + saga + staged reservation requests + the
/// <c>ProcessedMessage</c> record commit together; a failure rolls everything back
/// for redelivery, and a duplicate is acknowledged without re-execution).
/// </summary>
public sealed class OrderCreatedCompositeConsumer : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.HandleOrderCreated";

    private readonly IOrderSummaryProjectionService _projectionService;
    private readonly OrderSagaOrchestrator _orchestrator;

    public OrderCreatedCompositeConsumer(
        IOrderSummaryProjectionService projectionService,
        OrderSagaOrchestrator orchestrator)
    {
        _projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public string HandlerName => StableHandlerName;

    public async Task HandleAsync(
        OrderCreatedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        await _projectionService.ProjectAsync(
            @event.OrderId,
            envelope.CorrelationId,
            envelope.CausationId,
            cancellationToken).ConfigureAwait(false);

        await _orchestrator.StartFromOrderCreatedAsync(
            new OrderId(@event.OrderId),
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.OccurredAt,
            cancellationToken).ConfigureAwait(false);
    }
}
