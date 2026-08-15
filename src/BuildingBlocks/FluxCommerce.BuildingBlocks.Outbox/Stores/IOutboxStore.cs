using FluxCommerce.BuildingBlocks.Outbox.Messages;

namespace FluxCommerce.BuildingBlocks.Outbox.Stores;

/// <summary>
/// Phase 6.5 Outbox store surface. A naive pending query is insufficient for background publication:
/// the store must claim messages atomically, transition them through <see cref="OutboxStatus"/>
/// (Pending → InProgress → Published, or failure back to Pending), and recover claims left
/// <see cref="OutboxStatus.InProgress"/> by a crashed worker. All state transitions are conditional
/// updates guarded by the current status — the status acts as the optimistic concurrency token, so
/// concurrent workers can never claim or complete the same message twice.
/// </summary>
public interface IOutboxStore
{
    /// <summary>Persists a new outbox message (typically <see cref="OutboxStatus.Pending"/>).</summary>
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="batchSize"/> <see cref="OutboxStatus.Pending"/> messages ordered by
    /// creation time (oldest first). This is a read-only peek — it does not claim anything; use
    /// <see cref="ClaimPendingAsync"/> to take ownership of messages.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> pending messages for processing: a single
    /// conditional UPDATE transitions them to <see cref="OutboxStatus.InProgress"/> (recording the claim
    /// time in <see cref="OutboxMessage.ProcessedAtUtc"/>) and returns the claimed messages. Because the
    /// UPDATE is guarded by <c>Status = Pending</c>, a message can be claimed by exactly one worker even
    /// under concurrent dispatcher execution. Returns an empty list when nothing was pending.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conditionally claims a single message: Pending → InProgress (recording the claim time). Returns
    /// <see langword="true"/> only if the message was still <see cref="OutboxStatus.Pending"/>, so a
    /// message already claimed or processed is never re-claimed.
    /// </summary>
    Task<bool> MarkInProgressAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed message published: InProgress → Published (recording the completion time).
    /// Returns <see langword="true"/> only if the message was still <see cref="OutboxStatus.InProgress"/>.
    /// </summary>
    Task<bool> MarkPublishedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a retryable publication failure: InProgress → Pending, storing the error, clearing the
    /// claim time, and incrementing the retry count so the message becomes eligible to be claimed again.
    /// Returns <see langword="true"/> only if the message was still <see cref="OutboxStatus.InProgress"/>.
    /// </summary>
    Task<bool> MarkRetryableFailureAsync(Guid id, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a claimed message as terminally failed: InProgress → Failed, storing the error and the
    /// completion time. Returns <see langword="true"/> only if the message was still
    /// <see cref="OutboxStatus.InProgress"/>.
    /// </summary>
    Task<bool> MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers InProgress claims that are older than <paramref name="claimTimeout"/> back to
    /// <see cref="OutboxStatus.Pending"/> so a crashed worker's messages remain recoverable. Returns the
    /// number of claims recovered.
    /// </summary>
    Task<int> RecoverExpiredClaimsAsync(TimeSpan claimTimeout, CancellationToken cancellationToken = default);
}
