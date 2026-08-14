namespace FluxCommerce.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Provides the correlation id that ties together all events of a single business operation. In an
/// HTTP request it is the request's <c>X-Correlation-ID</c>; background operations (e.g. the
/// reservation expiration cycle) receive a freshly generated id. Phase 6.3 uses it to stamp the
/// <c>OutboxMessage.CorrelationId</c>; Phase 6.9 propagates it across the RabbitMQ boundary.
/// </summary>
public interface ICorrelationIdProvider
{
    Guid GetCorrelationId();
}
