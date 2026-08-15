using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Outbox;

/// <summary>
/// Phase 6.5/6.8 <see cref="IOutboxStore"/> backed by the Inventory DbContext. Claiming is atomic: a single
/// conditional UPDATE transitions a batch of pending rows to <see cref="OutboxStatus.InProgress"/> and
/// captures the claimed ids through an OUTPUT clause, so concurrent workers can never claim the same
/// message. A Pending row is only claimed once its Phase 6.8 backoff window has elapsed, so retries are
/// not attempted early. Every other state transition is a conditional update guarded by the current
/// status (the optimistic concurrency token); <see cref="OutboxMessage.ProcessedAtUtc"/> records the
/// claim time and feeds stale-claim recovery.
/// </summary>
public sealed class InventoryOutboxStore : IOutboxStore
{
    private readonly InventoryDbContext _context;

    public InventoryOutboxStore(InventoryDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _context.OutboxMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidBatchSize(batchSize);

        return await _context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.Status == OutboxStatus.Pending)
            .OrderBy(message => message.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidBatchSize(batchSize);

        var claimedAtUtc = DateTime.UtcNow;

        // Single atomic conditional UPDATE: the CTE picks the oldest pending batch and the WHERE status
        // guard re-checks each target row at update time, so a row already claimed by a concurrent worker
        // (Status = InProgress) can never be claimed twice. The OUTPUT clause returns exactly the rows
        // this statement claimed (now InProgress, claim time stamped) to the caller. The UPDLOCK, READPAST
        // hints make concurrent claimers pick disjoint batches: each worker locks the rows it is claiming
        // while the statement runs and skips rows other workers have already locked, so no worker wastes
        // its batch on candidates another worker is about to claim. A Pending row is only eligible while
        // its backoff window has elapsed (RetryAfterUtc is null or not in the future) — a message
        // scheduled for a future retry is never claimed early, even across dispatcher restarts.
        return await _context
            .Database
            .SqlQuery<OutboxMessage>(
                $"""
                 ;WITH ToClaim AS (
                     SELECT TOP ({batchSize}) [Id]
                     FROM [OutboxMessages] WITH (UPDLOCK, READPAST)
                     WHERE [Status] = {(int)OutboxStatus.Pending}
                       AND ([RetryAfterUtc] IS NULL OR [RetryAfterUtc] <= {claimedAtUtc})
                     ORDER BY [CreatedAtUtc]
                 )
                 UPDATE O
                 SET [Status] = {(int)OutboxStatus.InProgress},
                     [ProcessedAtUtc] = {claimedAtUtc}
                 OUTPUT INSERTED.[Id], INSERTED.[EventId], INSERTED.[Type], INSERTED.[Payload],
                        INSERTED.[CorrelationId], INSERTED.[CausationId], INSERTED.[OccurredAtUtc],
                        INSERTED.[CreatedAtUtc], INSERTED.[ProcessedAtUtc], INSERTED.[Status],
                        INSERTED.[RetryCount], INSERTED.[RetryAfterUtc], INSERTED.[Error]
                 FROM [OutboxMessages] O
                 INNER JOIN ToClaim ON ToClaim.[Id] = O.[Id]
                 WHERE O.[Status] = {(int)OutboxStatus.Pending}
                   AND (O.[RetryAfterUtc] IS NULL OR O.[RetryAfterUtc] <= {claimedAtUtc})
                 """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> MarkInProgressAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var claimedAtUtc = DateTime.UtcNow;

        return await _context.OutboxMessages
            .Where(message =>
                message.Id == id &&
                message.Status == OutboxStatus.Pending &&
                (message.RetryAfterUtc == null || message.RetryAfterUtc <= claimedAtUtc))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.Status, OutboxStatus.InProgress)
                    .SetProperty(message => message.ProcessedAtUtc, claimedAtUtc),
                cancellationToken)
            .ConfigureAwait(false) > 0;
    }

    public async Task<bool> MarkPublishedAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.OutboxMessages
            .Where(message => message.Id == id && message.Status == OutboxStatus.InProgress)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.Status, OutboxStatus.Published)
                    .SetProperty(message => message.ProcessedAtUtc, DateTime.UtcNow),
                cancellationToken)
            .ConfigureAwait(false) > 0;

    public async Task<bool> MarkRetryableFailureAsync(
        Guid id,
        string error,
        DateTime? retryAfterUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);

        return await _context.OutboxMessages
            .Where(message => message.Id == id && message.Status == OutboxStatus.InProgress)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.Status, OutboxStatus.Pending)
                    .SetProperty(message => message.ProcessedAtUtc, (DateTime?)null)
                    .SetProperty(message => message.RetryCount, message => message.RetryCount + 1)
                    .SetProperty(message => message.RetryAfterUtc, retryAfterUtc)
                    .SetProperty(message => message.Error, error),
                cancellationToken)
            .ConfigureAwait(false) > 0;
    }

    public async Task<bool> MarkFailedAsync(
        Guid id,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);

        return await _context.OutboxMessages
            .Where(message => message.Id == id && message.Status == OutboxStatus.InProgress)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.Status, OutboxStatus.Failed)
                    .SetProperty(message => message.ProcessedAtUtc, DateTime.UtcNow)
                    .SetProperty(message => message.Error, error),
                cancellationToken)
            .ConfigureAwait(false) > 0;
    }

    public async Task<int> RecoverExpiredClaimsAsync(
        TimeSpan claimTimeout,
        CancellationToken cancellationToken = default)
    {
        if (claimTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(claimTimeout),
                "The claim timeout must be greater than zero.");

        var cutoff = DateTime.UtcNow - claimTimeout;

        return await _context.OutboxMessages
            .Where(message =>
                message.Status == OutboxStatus.InProgress &&
                message.ProcessedAtUtc < cutoff)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.Status, OutboxStatus.Pending)
                    .SetProperty(message => message.ProcessedAtUtc, (DateTime?)null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ThrowIfInvalidBatchSize(int batchSize)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "The batch size must be greater than zero.");
    }
}