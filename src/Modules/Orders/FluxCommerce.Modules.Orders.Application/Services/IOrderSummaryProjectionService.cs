namespace FluxCommerce.Modules.Orders.Application.Services;

/// <summary>
/// Phase 7.9: the transport-agnostic business operation behind the Order Summary projection consumer.
/// It reads the source Order aggregate through the Orders persistence port and stages the denormalized
/// projection row. It never touches messaging, RabbitMQ, or the DbContext directly: the consumer
/// infrastructure invokes it inside the idempotent-processing boundary, so this service only mutates
/// the current unit of work and the boundary commits it atomically with the processed-message record.
/// </summary>
public interface IOrderSummaryProjectionService
{
    /// <summary>
    /// Builds and stages the projection for the order identified by <paramref name="orderId"/>. The
    /// correlation/causation trace identifiers of the triggering delivery (Phase 7.8) are carried into
    /// the projection. Throws <see cref="InvalidOperationException"/> when the order does not exist so
    /// the consumer treats the delivery as a retryable failure.
    /// </summary>
    Task ProjectAsync(
        Guid orderId,
        Guid correlationId,
        Guid? causationId,
        CancellationToken cancellationToken = default);
}