using FluxCommerce.BuildingBlocks.Idempotency.Messages;

namespace FluxCommerce.BuildingBlocks.Idempotency.Processing;

/// <summary>
/// The atomic idempotent-processing boundary (Phase 7.4). It executes a business operation and records
/// the processed message in the SAME database transaction, so a successful processing commits Business
/// State + ProcessedMessage together and a business failure rolls both back. A message that was already
/// processed for the same <c>(Key, Handler)</c> identity is acknowledged as a duplicate WITHOUT
/// re-executing the business operation; a concurrent duplicate race is resolved by the database unique
/// constraint and reported as already-processed (exactly one business effect, exactly one processed
/// message). The implementation is transport-agnostic — the business operation is a plain delegate that
/// runs inside the transaction, so the future consumer infrastructure can invoke any application handler
/// through this boundary without knowing the persistence details.
/// </summary>
/// <remarks>
/// Idempotency is deliberately modeled as three surfaces, not one:
/// <see cref="FluxCommerce.BuildingBlocks.Idempotency.Stores.IIdempotencyStore"/>
/// is the persistence CRUD port,
/// <see cref="FluxCommerce.BuildingBlocks.Idempotency.Detection.IDuplicateDetector"/>
/// is the claim/check policy,
/// and this processor is the ONLY atomic business boundary consumers may invoke. Merging them would
/// couple querying, claiming, and transacted execution into a single abstraction.
/// </remarks>
public interface IIdempotentProcessor
{
    /// <summary>
    /// Atomically claims <paramref name="message"/> and executes <paramref name="businessOperation"/>
    /// inside one transaction. Returns <see cref="IdempotencyResult.NotProcessed"/> when the message was
    /// newly processed (business operation executed and committed together with the processed-message
    /// record), or <see cref="IdempotencyResult.AlreadyProcessed"/> when the message was already
    /// processed for the same <c>(Key, Handler)</c> identity (business operation NOT executed).
    /// </summary>
    /// <param name="message">The processed-message record to claim; <see cref="ProcessedMessage.Handler"/>
    /// must be provided because the idempotency identity is <c>EventId + HandlerName</c>.</param>
    /// <param name="businessOperation">The business operation to run inside the transaction. It must
    /// perform its state changes through the same persistence scope/DbContext as the processor so the
    /// single commit is truly atomic.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="message"/> or
    /// <paramref name="businessOperation"/> is <c>null</c>, or <paramref name="message"/> has no
    /// handler.</exception>
    /// <exception cref="ArgumentException">When <paramref name="message"/> has no key.</exception>
    Task<IdempotencyResult> TryProcessAsync(
        ProcessedMessage message,
        Func<Task> businessOperation,
        CancellationToken cancellationToken = default);
}