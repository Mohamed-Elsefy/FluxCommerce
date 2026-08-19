using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using FluxCommerce.Modules.Orders.Contracts.IntegrationEvents;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;

namespace FluxCommerce.Modules.Orders.Infrastructure.Consuming;

/// <summary>
/// Phase 8.3 the consumer that boots the Order/Inventory Saga. It is a thin transport-agnostic adapter:
/// it reads the order id and the Phase 7.8 trace identifiers from the contract and envelope and delegates
/// the whole orchestration to <see cref="OrderSagaOrchestrator"/>. Its stable <see cref="HandlerName"/>
/// scopes the idempotency identity (<c>EventId + HandlerName</c>) enforced by the consumer
/// infrastructure, so a redelivered <c>orders.order_created</c> is detected before the orchestrator is
/// ever reached.
/// </summary>
public sealed class OrderCreatedSagaConsumer : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    public const string StableHandlerName = "Orders.RunOrderSaga";

    private readonly OrderSagaOrchestrator _orchestrator;

    public OrderCreatedSagaConsumer(OrderSagaOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public string HandlerName => StableHandlerName;

    public Task HandleAsync(
        OrderCreatedIntegrationEvent @event,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(envelope);

        return _orchestrator.StartFromOrderCreatedAsync(
            new OrderId(@event.OrderId),
            envelope.ProducedEventCorrelationId,
            envelope.ProducedEventCausationId,
            @event.OccurredAt,
            cancellationToken);
    }
}