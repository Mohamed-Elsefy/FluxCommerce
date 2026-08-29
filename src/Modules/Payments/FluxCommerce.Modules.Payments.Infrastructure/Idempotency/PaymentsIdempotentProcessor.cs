using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Payments.Infrastructure.Idempotency;

/// <summary>
/// Phase 10.3 <see cref="IIdempotentProcessor"/> backed by the Payments DbContext.
/// Mirrors <c>OrdersIdempotentProcessor</c>: Business State (Payment) + ProcessedMessage
/// commit atomically inside a retrying execution strategy. Duplicate <c>(Key,Handler)</c>
/// is detected upfront and on race via primary key.
/// </summary>
public sealed class PaymentsIdempotentProcessor : IIdempotentProcessor
{
    private readonly PaymentsDbContext _context;
    private readonly PaymentsDuplicateDetector _duplicateDetector;
    private readonly PaymentsIdempotencyStore _idempotencyStore;

    public PaymentsIdempotentProcessor(PaymentsDbContext context, PaymentsDuplicateDetector duplicateDetector, PaymentsIdempotencyStore idempotencyStore)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _duplicateDetector = duplicateDetector ?? throw new ArgumentNullException(nameof(duplicateDetector));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
    }

    public Task<IdempotencyResult> TryProcessAsync(ProcessedMessage message, Func<Task> businessOperation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(businessOperation);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Handler);

        var executionStrategy = _context.Database.CreateExecutionStrategy();

        return executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var alreadyProcessed = await _duplicateDetector.IsProcessedAsync(message.Key, message.Handler, cancellationToken).ConfigureAwait(false);
            if (alreadyProcessed)
            {
                var winner = await _idempotencyStore.GetAsync(message.Key, message.Handler, cancellationToken).ConfigureAwait(false);
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
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _context.ChangeTracker.Clear();
                var winner = await _idempotencyStore.GetAsync(message.Key, message.Handler, cancellationToken).ConfigureAwait(false);
                if (winner is null) throw;
                return IdempotencyResult.AlreadyProcessed(winner.ProcessedAtUtc);
            }
        });
    }
}
