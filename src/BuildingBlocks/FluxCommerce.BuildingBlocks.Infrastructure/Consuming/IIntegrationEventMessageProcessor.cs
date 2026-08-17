using RabbitMQ.Client;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Scoped transport-agnostic boundary that turns a single RabbitMQ delivery into a consumer processing
/// action. It resolves the event contract from the wire <c>EventType</c>, validates the transport
/// metadata and payload, resolves the registered handler and — through the canonical
/// <see cref="FluxCommerce.BuildingBlocks.Idempotency.Processing.IIdempotentProcessor"/> boundary —
/// invokes the handler inside the module's atomic processing transaction. The outcome is a
/// <see cref="ConsumerMessageAction"/> the consumer host applies on the delivery channel; the processor
/// itself never touches the broker.
/// </summary>
public interface IIntegrationEventMessageProcessor
{
    Task<ConsumerMessageAction> ProcessAsync(
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);
}