using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.BuildingBlocks.Messaging.Consuming;

/// <summary>
/// Contract for a module integration event handler. A handler owns a single business reaction to a
/// single integration event contract and is transport-agnostic: it receives the deserialized contract
/// plus its processing context (<see cref="IntegrationEventEnvelope"/>), never a broker message.
/// <see cref="HandlerName"/> is the stable consumer/handler identity used to scope idempotency
/// (idempotency identity = <c>EventId + HandlerName</c>), so it must be unique and stable per handler
/// type. Infrastructure resolves the concrete handler from the container and invokes
/// <see cref="HandleAsync"/>, so handlers are plain DI-registered types without reflection-based
/// activation.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class, IIntegrationEvent
{
    string HandlerName { get; }

    Task HandleAsync(TEvent @event, IntegrationEventEnvelope envelope, CancellationToken cancellationToken = default);
}
