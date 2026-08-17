using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;

/// <summary>
/// Phase 7.3 <see cref="IIdempotencyStore"/> backed by the Inventory DbContext. This is the
/// check-then-act read/write surface (<see cref="GetAsync"/>/<see cref="AddAsync"/>); it is NOT
/// sufficient as the sole concurrency protection. The composite <c>(Key, Handler)</c> primary key is
/// the final arbiter, so <see cref="AddAsync"/> treats a unique-constraint race as an already-processed
/// duplicate (re-reading the winner) instead of an application failure — the same precedent as
/// <c>CheckoutStore.PersistAsync</c>. Atomic claim semantics belong to
/// <c>IDuplicateDetector.TryClaimAsync</c> (Phase 7.4 composition).
/// </summary>
public sealed class InventoryIdempotencyStore : IIdempotencyStore
{
    private readonly InventoryDbContext _context;

    public InventoryIdempotencyStore(InventoryDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ProcessedMessage?> GetAsync(
        string key,
        string? handler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (handler is null)
        {
            return await _context.ProcessedMessages
                .AsNoTracking()
                .Where(message => message.Key == key)
                .OrderByDescending(message => message.ProcessedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return await _context.ProcessedMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(
                message => message.Key == key && message.Handler == handler,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _context.ProcessedMessages.Add(message);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        }
    }
}