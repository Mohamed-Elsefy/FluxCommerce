using RabbitMQ.Client;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Publishes a dead-lettered copy of a delivery to the consumer's dead-letter exchange (DLX) so the
/// dead-letter queue (DLQ) preserves the FULL original message (body + headers) enriched with terminal
/// failure metadata (reason, attempt, error, occurred-at) for diagnosis. Used by the message processor
/// on the terminal failure path (permanent failure / exhausted retries). Returns <c>true</c> only when
/// the broker confirms the publish; when it fails, the processor falls back to rejecting the original
/// delivery (Nack requeue:false) so the broker's <c>x-dead-letter-exchange</c> preserves it verbatim —
/// a terminal failure can never silently lose or endlessly retry a message.
/// </summary>
public interface IIntegrationEventDeadLetterer
{
    /// <summary>
    /// Publishes the dead-lettered copy of <paramref name="originalProperties"/>/<paramref name="body"/>
    /// to the DLX with the original headers preserved and <paramref name="additionalHeaders"/> merged on
    /// top (the enrichment wins on conflicts). The routing key is preserved so the DLQ message stays
    /// correlated to its event type.
    /// </summary>
    Task<bool> PublishAsync(
        IReadOnlyBasicProperties originalProperties,
        ReadOnlyMemory<byte> body,
        string routingKey,
        IReadOnlyDictionary<string, object?> additionalHeaders,
        CancellationToken cancellationToken = default);
}
