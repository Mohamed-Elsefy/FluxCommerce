namespace FluxCommerce.BuildingBlocks.Messaging.Consuming;

/// <summary>
/// Resolves a consumer descriptor for a wire event type. This is the deterministic, explicit
/// replacement for reflection-based handler discovery: for a given stable <c>EventType</c> (e.g.
/// <c>orders.order_created</c>) it returns the contract to deserialize and the handler type to resolve.
/// A missing mapping returns <c>null</c> so the consumer can skip unknown contracts without failing.
/// Implementations are registered in the container and may be composed from a per-module handler
/// mapping registry in a later sub-phase.
/// </summary>
public interface IIntegrationEventConsumerRegistry
{
    IntegrationEventConsumerDescriptor? Resolve(string eventType);
}
