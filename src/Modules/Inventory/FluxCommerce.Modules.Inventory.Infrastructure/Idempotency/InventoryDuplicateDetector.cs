using FluxCommerce.BuildingBlocks.Idempotency.Detection;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;

/// <summary>
/// Phase 7.3 <see cref="IDuplicateDetector"/> backed by the Inventory DbContext. Duplicate detection is
/// keyed on the composite <c>(Key, Handler)</c> identity (EventId + HandlerName). The minimal atomic
/// claim required by the abstraction is a plain insert guarded by the composite primary key: the first
/// concurrent consumer to commit wins, and every other attempt violates the unique constraint and is
/// re-read as <see cref="IdempotencyResult.AlreadyProcessed"/> — the database is the final concurrency
/// protection, never a check-then-act existence test. The full Business State + ProcessedMessage atomic
/// processing pipeline belongs to Phase 7.4.
/// </summary>
public sealed class InventoryDuplicateDetector : IDuplicateDetector
{
    private readonly InventoryDbContext _context;

    public InventoryDuplicateDetector(InventoryDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IdempotencyResult> TryClaimAsync(
        ProcessedMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _context.ProcessedMessages.Add(message);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return IdempotencyResult.NotProcessed();
        }
        catch (DbUpdateException)
        {
            var existing = await _context.ProcessedMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.Key == message.Key && candidate.Handler == message.Handler,
                    cancellationToken)
                .ConfigureAwait(false);

            _context.ChangeTracker.Clear();

            if (existing is null)
                throw;

            return IdempotencyResult.AlreadyProcessed(existing.ProcessedAtUtc);
        }
    }

    public async Task<bool> IsProcessedAsync(
        string key,
        string? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var query = _context.ProcessedMessages
            .AsNoTracking()
            .Where(message => message.Key == key);

        if (handler is not null)
        {
            query = query.Where(message => message.Handler == handler);
        }

        return await query.AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}