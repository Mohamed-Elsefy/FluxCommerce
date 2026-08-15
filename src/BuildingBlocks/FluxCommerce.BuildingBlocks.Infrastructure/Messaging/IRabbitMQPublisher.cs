using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Publishes integration events to RabbitMQ using a durable topic exchange and publisher confirms.
/// <para>
/// The <c>eventType</c> is the stable, versioned-on-routing type identifier (the concrete
/// event's <c>EventType</c> constant) and doubles as the topic routing key, so consumers bind their
/// queues with routing patterns over event types. The event body is serialized from its runtime
/// concrete type (never from the <see cref="IIntegrationEvent"/> interface) so the business payload
/// survives transport, matching the Outbox payload contract.
/// </para>
/// <para>
/// Result semantics: <see langword="true"/> is returned only when the broker confirmed the message
/// (publisher confirm received). An explicit broker nack/return yields <see langword="false"/>. Any
/// infrastructure failure (unreachable broker, channel/connection error) throws instead, so a caller
/// never mistakes an attempted publish for a confirmed one.
/// </para>
/// </summary>
public interface IRabbitMQPublisher
{
    Task<bool> PublishAsync(string eventType, IIntegrationEvent @event, CancellationToken cancellationToken = default);
}
