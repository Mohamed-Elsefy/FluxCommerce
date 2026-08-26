using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Payments.Infrastructure.Idempotency;

/// <summary>
/// Phase 10.3 <see cref="IIdempotencyStore"/> backed by the Payments DbContext.
/// Mirrors <c>OrdersIdempotencyStore</c>.
/// </summary>
public sealed class PaymentsIdempotencyStore : IIdempotencyStore
{
    private readonly PaymentsDbContext _context;

    public PaymentsIdempotencyStore(PaymentsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ProcessedMessage?> GetAsync(string key, string? handler = null, CancellationToken cancellationToken = default)
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
            .FirstOrDefaultAsync(message => message.Key == key && message.Handler == handler, cancellationToken)
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
                .FirstOrDefaultAsync(candidate => candidate.Key == message.Key && candidate.Handler == message.Handler, cancellationToken)
                .ConfigureAwait(false);
            _context.ChangeTracker.Clear();
            if (existing is null) throw;
        }
    }
}
