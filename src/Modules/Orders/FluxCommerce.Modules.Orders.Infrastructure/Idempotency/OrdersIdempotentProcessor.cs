using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Orders.Infrastructure.Idempotency;

/// <summary>
/// Phase 7.9 <see cref="IIdempotentProcessor"/> backed by the Orders DbContext. It establishes the
/// atomic processing boundary for the Order Summary projection consumer: Business State (the
/// projection row) + ProcessedMessage commit in the SAME transaction. A known duplicate
/// <c>(Key, Handler)</c> (EventId + HandlerName) is detected up front and acknowledged WITHOUT
/// re-executing the business operation; a concurrent duplicate race is resolved by the composite
/// <c>(Key, Handler)</c> primary key — the losing attempt rolls back, re-reads the winner and reports
/// <c>AlreadyProcessed</c>. A business failure propagates and rolls the whole transaction back, so a
/// failed attempt leaves no processed-message record and the message stays retryable. The transaction
/// is executed through the retrying execution strategy (same precedent as the Inventory processor and
/// <c>CheckoutStore.PersistAsync</c>), because user-initiated transactions are not allowed outside it.
/// </summary>
public sealed class OrdersIdempotentProcessor : IIdempotentProcessor
{
    private readonly OrdersDbContext _context;
    private readonly OrdersDuplicateDetector _duplicateDetector;
    private readonly OrdersIdempotencyStore _idempotencyStore;

    public OrdersIdempotentProcessor(
        OrdersDbContext context,
        OrdersDuplicateDetector duplicateDetector,
        OrdersIdempotencyStore idempotencyStore)
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