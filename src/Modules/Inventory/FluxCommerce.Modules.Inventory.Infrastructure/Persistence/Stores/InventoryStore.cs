using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="IInventoryStore"/> backed by the Inventory DbContext. Reads load tracked aggregates so
/// a stock adjustment or a reservation lifecycle mutation can be applied and flushed through the same
/// context; a reservation is added to the tracked set and persisted together with its mutated
/// inventory item on a single <c>SaveChangesAsync</c>, making each reservation operation atomic.
/// </summary>
public sealed class InventoryStore : IInventoryStore
{
    private readonly InventoryDbContext _context;

    public InventoryStore(InventoryDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<InventoryItem?> GetItemBySkuAsync(
        InventorySku sku,
        CancellationToken cancellationToken = default)
    {
        return await _context.InventoryItems
            .FirstOrDefaultAsync(item => item.Id == sku, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task AddItemAsync(
        InventoryItem item,
        CancellationToken cancellationToken = default)
    {
        _context.InventoryItems.Add(item);

        return Task.CompletedTask;
    }

    public async Task<InventoryReservation?> GetReservationByIdAsync(
        InventoryReservationId reservationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.InventoryReservations
            .FirstOrDefaultAsync(reservation => reservation.Id == reservationId, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> ExistsAsync(
        InventorySku sku,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sku);

        return _context.InventoryItems
            .AnyAsync(item => item.Id == sku, cancellationToken);
    }

    public Task AddReservationAsync(
        InventoryReservation reservation,
        CancellationToken cancellationToken = default)
    {
        _context.InventoryReservations.Add(reservation);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<InventoryReservationId>> GetExpiredReservationIdsAsync(
        DateTimeOffset utcNow,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                "The maximum number of results must be greater than zero.");

        // The (Status, ExpiresAt) index serves exactly this lookup; only pending reservations that
        // are already past their deadline can require expiration.
        return await _context.InventoryReservations
            .Where(reservation =>
                reservation.Status == InventoryReservationStatus.Pending &&
                reservation.ExpiresAt <= utcNow)
            .OrderBy(reservation => reservation.ExpiresAt)
            .Take(maxResults)
            .Select(reservation => reservation.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<InventoryReservation?> TryReserveAsync(
        InventorySku sku,
        InventoryReservationId reservationId,
        OrderId orderId,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(reservationId);
        ArgumentNullException.ThrowIfNull(orderId);

        // When the caller has already established a transaction (for example the consumer idempotency
        // boundary), the reservation participates in that ambient transaction instead of opening a
        // nested one: the atomic gate UPDATE and the reservation insert then commit atomically with
        // the ProcessedMessage, and a business failure rolls the whole unit back with it.
        if (_context.Database.CurrentTransaction is not null)
        {
            return ReserveCoreAsync(
                sku, reservationId, orderId, quantity, expiresAt, utcNow, cancellationToken);
        }

        // The execution strategy is used because the retry strategy on this context does not allow
        // user-initiated transactions outside of it; the whole gate + insert is a retriable unit.
        var executionStrategy = _context.Database.CreateExecutionStrategy();

        return executionStrategy.ExecuteAsync(
            async () =>
            {
                await using var transaction = await _context
                    .Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                var reservation = await ReserveCoreAsync(
                    sku, reservationId, orderId, quantity, expiresAt, utcNow, cancellationToken)
                    .ConfigureAwait(false);

                if (reservation is null)
                {
                    await transaction
                        .RollbackAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return null;
                }

                await transaction
                    .CommitAsync(cancellationToken)
                    .ConfigureAwait(false);

                return reservation;
            });
    }

    private async Task<InventoryReservation?> ReserveCoreAsync(
        InventorySku sku,
        InventoryReservationId reservationId,
        OrderId orderId,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        // The stock check and the corresponding mutation are a single atomic conditional
        // UPDATE: the row is only touched while the requested quantity is still available,
        // so two concurrent reservations can never both observe enough stock. RowsAffected
        // is the success gate.
        var affected = await _context
            .Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE [InventoryItems]
                 SET [ReservedQuantity] = [ReservedQuantity] + {quantity},
                     [Version] = [Version] + 1,
                     [UpdatedAtUtc] = {utcNow}
                 WHERE [Sku] = {sku.Value}
                   AND [ReservedQuantity] + {quantity} <= [OnHandQuantity]
                 """,
                cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
            return null;

        // Re-load the item so the reservation is recorded against the exact post-gate
        // quantities. The reservation is constructed without mutating the item again: the
        // reserved-quantity increment was already applied atomically by the gate, and the
        // domain records the creation event. The item is intentionally not tracked in this
        // context, so nothing re-saves it.
        var item = await _context.InventoryItems
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == sku, cancellationToken)
            .ConfigureAwait(false);

        var reservation = InventoryReservation.CreateReserved(
            reservationId,
            orderId,
            item.Sku,
            quantity,
            expiresAt,
            utcNow).Value;

        _context.InventoryReservations.Add(reservation);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return reservation;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> SaveLifecycleChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            return false;
        }
    }
}
