using FluxCommerce.BuildingBlocks.Idempotency.Detection;
using FluxCommerce.BuildingBlocks.Idempotency.Messages;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Payments.Infrastructure.Idempotency;

/// <summary>
/// Phase 10.3 <see cref="IDuplicateDetector"/> backed by the Payments DbContext.
/// Mirrors <c>OrdersDuplicateDetector</c>.
/// </summary>
public sealed class PaymentsDuplicateDetector : IDuplicateDetector
{
    private readonly PaymentsDbContext _context;

    public PaymentsDuplicateDetector(PaymentsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IdempotencyResult> TryClaimAsync(ProcessedMessage message, CancellationToken cancellationToken = default)
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
                .FirstOrDefaultAsync(candidate => candidate.Key == message.Key && candidate.Handler == message.Handler, cancellationToken)
                .ConfigureAwait(false);
            _context.ChangeTracker.Clear();
            if (existing is null) throw;
            return IdempotencyResult.AlreadyProcessed(existing.ProcessedAtUtc);
        }
    }

    public async Task<bool> IsProcessedAsync(string key, string? handler = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var query = _context.ProcessedMessages.AsNoTracking().Where(message => message.Key == key);
        if (handler is not null) query = query.Where(message => message.Handler == handler);
        return await query.AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}
