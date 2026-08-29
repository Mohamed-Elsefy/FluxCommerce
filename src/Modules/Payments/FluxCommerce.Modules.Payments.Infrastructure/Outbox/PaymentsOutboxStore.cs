using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Payments.Infrastructure.Outbox;

/// <summary>
/// Phase 10.3 <see cref="IOutboxStore"/> backed by the Payments DbContext. Mirrors
/// <c>OrdersOutboxStore</c> / <c>InventoryOutboxStore</c> — atomic claiming via conditional
/// UPDATE with OUTPUT, pending eligibility guarded by <c>RetryAfterUtc</c>, status as
/// optimistic concurrency token. Enables reliable publication of <c>payments.*</c> events
/// staged by the webhook processor (Phase 9.11) and future payment refund flows.
/// </summary>
public sealed class PaymentsOutboxStore : IOutboxStore
{
    private readonly PaymentsDbContext _context;

    public PaymentsOutboxStore(PaymentsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _context.OutboxMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
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

    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidBatchSize(batchSize);
        var claimedAtUtc = DateTime.UtcNow;

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
                         INSERTED.[RetryCount], INSERTED.[RetryAfterUtc], INSERTED.[Error],
                         INSERTED.[TraceParent], INSERTED.[TraceState]
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

    public async Task<bool> MarkRetryableFailureAsync(Guid id, string error, DateTime? retryAfterUtc = null, CancellationToken cancellationToken = default)
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

    public async Task<bool> MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
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

    public async Task<int> RecoverExpiredClaimsAsync(TimeSpan claimTimeout, CancellationToken cancellationToken = default)
    {
        if (claimTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimTimeout), "The claim timeout must be greater than zero.");
        var cutoff = DateTime.UtcNow - claimTimeout;
        return await _context.OutboxMessages
            .Where(message => message.Status == OutboxStatus.InProgress && message.ProcessedAtUtc < cutoff)
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
            throw new ArgumentOutOfRangeException(nameof(batchSize), "The batch size must be greater than zero.");
    }
}
