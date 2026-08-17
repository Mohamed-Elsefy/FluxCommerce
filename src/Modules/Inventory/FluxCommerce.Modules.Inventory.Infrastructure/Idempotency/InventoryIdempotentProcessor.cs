using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;

/// <summary>
/// Phase 7.4 <see cref="IIdempotentProcessor"/> backed by the Inventory DbContext. It establishes the
/// atomic processing boundary: Business State + ProcessedMessage commit in the SAME transaction. A known
/// duplicate <c>(Key, Handler)</c> (EventId + HandlerName) is detected up front and acknowledged WITHOUT
/// re-executing the business operation; a concurrent duplicate race is resolved by the composite
/// <c>(Key, Handler)</c> primary key (the same race handling as <c>CheckoutStore.PersistAsync</c>) — the
/// losing attempt rolls back, re-reads the winner and reports <c>AlreadyProcessed</c>. A business failure
/// propagates and rolls the whole transaction back, so a failed attempt leaves no processed-message
/// record and the message stays retryable. The transaction is executed through the retrying execution
/// strategy (same precedent as <c>InventoryStore.TryReserveAsync</c>), because user-initiated
/// transactions are not allowed outside it.
/// </summary>
public sealed class InventoryIdempotentProcessor : IIdempotentProcessor
{
    private readonly InventoryDbContext _context;
    private readonly InventoryDuplicateDetector _duplicateDetector;
    private readonly InventoryIdempotencyStore _idempotencyStore;

    public InventoryIdempotentProcessor(
        InventoryDbContext context,
        InventoryDuplicateDetector duplicateDetector,
        InventoryIdempotencyStore idempotencyStore)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _duplicateDetector = duplicateDetector ?? throw new ArgumentNullException(nameof(duplicateDetector));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
    }

    public Task<IdempotencyResult> TryProcessAsync(
        ProcessedMessage message,
        Func<Task> businessOperation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(businessOperation);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Handler);

        var executionStrategy = _context.Database.CreateExecutionStrategy();

        return executionStrategy.ExecuteAsync(
            async () =>
            {
                await using var transaction = await _context
                    .Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Fast-path duplicate check: a message that is already processed must not re-execute
                // the business operation. The database constraint remains the final concurrency
                // arbiter for the race that can still happen after this read.
                var alreadyProcessed = await _duplicateDetector
                    .IsProcessedAsync(message.Key, message.Handler, cancellationToken)
                    .ConfigureAwait(false);

                if (alreadyProcessed)
                {
                    var winner = await _idempotencyStore
                        .GetAsync(message.Key, message.Handler, cancellationToken)
                        .ConfigureAwait(false);

                    return IdempotencyResult.AlreadyProcessed(winner?.ProcessedAtUtc ?? message.ProcessedAtUtc);
                }

                _context.ProcessedMessages.Add(message);

                try
                {
                    await businessOperation().ConfigureAwait(false);
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return IdempotencyResult.NotProcessed();
                }
                catch (DbUpdateException)
                {
                    await transaction
                        .RollbackAsync(cancellationToken)
                        .ConfigureAwait(false);

                    _context.ChangeTracker.Clear();

                    var winner = await _idempotencyStore
                        .GetAsync(message.Key, message.Handler, cancellationToken)
                        .ConfigureAwait(false);

                    if (winner is null)
                        throw;

                    return IdempotencyResult.AlreadyProcessed(winner.ProcessedAtUtc);
                }
            });
    }
}