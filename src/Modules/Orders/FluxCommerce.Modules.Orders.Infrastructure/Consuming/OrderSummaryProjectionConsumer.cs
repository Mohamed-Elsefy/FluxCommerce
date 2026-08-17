using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 7.9: the first real integration event consumer — the Order Summary projection consumer. It is
/// a thin transport-agnostic adapter: it reads the order id and the Phase 7.8 trace identifiers from
/// the contract and envelope and delegates the entire business operation to
/// <see cref="IOrderSummaryProjectionService"/> (Application). It never touches RabbitMQ, messaging
/// transport types, or the DbContext directly, and its stable <see cref="HandlerName"/> scopes the
/// idempotency identity (<c>EventId + HandlerName</c>) enforced by the consumer infrastructure.
/// </summary>
public sealed class OrderSummaryProjectionConsumer : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.ProjectOrderSummary";

    private readonly IOrderSummaryProjectionService _projectionService;

    public OrderSummaryProjectionConsumer(IOrderSummaryProjectionService projectionService)
    {
        _projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        OrderCreatedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        return _projectionService.ProjectAsync(
            @event.OrderId,
            envelope.CorrelationId,
            envelope.CausationId,
            cancellationToken);
    }
}