using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Mapping;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
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

    public async Task<IReadOnlyList<InventoryReservation>> GetReservationsByIdsAsync(
        IReadOnlyCollection<InventoryReservationId> reservationIds,
        CancellationToken cancellationToken = default)
    {
        if (reservationIds is null)
            throw new ArgumentNullException(nameof(reservationIds));

        if (reservationIds.Count == 0)
            return Array.Empty<InventoryReservation>();

        return await _context.InventoryReservations
            .Where(reservation => reservationIds.Contains(reservation.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InventoryItem>> GetItemsBySkusAsync(
        IReadOnlyCollection<InventorySku> skus,
        CancellationToken cancellationToken = default)
    {
        if (skus is null)
            throw new ArgumentNullException(nameof(skus));

        if (skus.Count == 0)
            return Array.Empty<InventoryItem>();

        return await _context.InventoryItems
            .Where(item => skus.Contains(item.Id))
            .ToListAsync(cancellationToken)
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

    /// <summary>
    /// Administrative item list ordered by SKU. Converted value-object members (such as the SKU
    /// string) cannot be referenced inside a LINQ-to-Entities query, so filtering and ordering run
    /// in two phases: matching keys stream from the database (scalar filters stay in the database)
    /// and substring search plus SKU ordering run in memory over the lightweight keys before only
    /// the page's aggregates are fetched. Reads use the row mappers so no query projection touches
    /// unmapped members.
    /// </summary>
    public async Task<PagedResult<InventoryItemReadModel>> GetItemsPageAsync(
        string? search,
        int? maxAvailable,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.InventoryItems.AsNoTracking();

        if (maxAvailable.HasValue)
            baseQuery = baseQuery.Where(item => item.OnHandQuantity - item.ReservedQuantity <= maxAvailable.Value);

        var keys = await baseQuery
            .Select(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var filtered = keys.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            filtered = filtered.Where(sku => sku.Value.Contains(term, StringComparison.Ordinal));
        }

        var orderedIds = filtered
            .OrderBy(sku => sku.Value, StringComparer.Ordinal)
            .ToList();

        var totalCount = orderedIds.Count;

        var pageIds = orderedIds
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        if (pageIds.Count == 0)
            return new PagedResult<InventoryItemReadModel>([], totalCount, pageNumber, pageSize);

        var items = await _context.InventoryItems
            .AsNoTracking()
            .Where(item => pageIds.Contains(item.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var itemBySku = items.ToDictionary(
            item => item.Sku.Value,
            item => InventoryItemReadModelMapper.ToReadModel(item),
            StringComparer.Ordinal);

        var orderedItems = pageIds
            .Select(sku => itemBySku[sku.Value])
            .ToList();

        return new PagedResult<InventoryItemReadModel>(orderedItems, totalCount, pageNumber, pageSize);
    }

    /// <summary>
    /// Administrative reservation list. Converted value-object members cannot be referenced inside
    /// a LINQ-to-Entities query, so the newest-first window runs over whole-identity keys streamed
    /// from the database and only the page's aggregates are fetched and mapped in memory.
    /// </summary>
    public async Task<PagedResult<InventoryReservationReadModel>> GetReservationsPageAsync(
        InventoryReservationStatus? status,
        InventorySku? sku,
        OrderId? orderId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.InventoryReservations.AsNoTracking();

        if (status.HasValue)
            baseQuery = baseQuery.Where(reservation => reservation.Status == status.Value);

        if (sku is not null)
            baseQuery = baseQuery.Where(reservation => reservation.Sku == sku);

        if (orderId is not null)
            baseQuery = baseQuery.Where(reservation => reservation.OrderId == orderId);

        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var skip = (pageNumber - 1) * pageSize;
        if (skip >= totalCount)
            return new PagedResult<InventoryReservationReadModel>([], totalCount, pageNumber, pageSize);

        // Bounded newest-first window: at most skip+take keys in memory regardless of history size.
        var windowSize = Math.Min(skip + pageSize, totalCount);
        var window = new List<ReservationSortKey>(windowSize);

        await foreach (var key in baseQuery
            .Select(reservation => new ReservationSortKey(reservation.Id, reservation.CreatedAtUtc))
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            var index = window.BinarySearch(key, ReservationNewestFirstComparer);
            if (index < 0)
                index = ~index;

            if (index < windowSize)
            {
                window.Insert(index, key);
                if (window.Count > windowSize)
                    window.RemoveAt(window.Count - 1);
            }
        }

        var pageKeys = window.Skip(skip).Take(pageSize).ToList();

        var pageReservationIds = pageKeys.Select(key => key.ReservationId).ToList();

        var reservations = await _context.InventoryReservations
            .AsNoTracking()
            .Where(reservation => pageReservationIds.Contains(reservation.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var itemByReservationId = reservations.ToDictionary(
            reservation => reservation.Id.Value,
            reservation => InventoryReservationReadModelMapper.ToReadModel(reservation));

        var orderedItems = pageKeys
            .Select(key => itemByReservationId[key.ReservationId.Value])
            .ToList();

        return new PagedResult<InventoryReservationReadModel>(orderedItems, totalCount, pageNumber, pageSize);
    }

    public async Task<InventoryCountsReadModel> GetCountsAsync(
        int lowStockThreshold,
        CancellationToken cancellationToken = default)
    {
        var items = _context.InventoryItems.AsNoTracking();

        var totalItems = await items.CountAsync(cancellationToken).ConfigureAwait(false);

        var totalOnHand = await items.SumAsync(item => item.OnHandQuantity, cancellationToken).ConfigureAwait(false);

        var totalReserved = await items.SumAsync(item => item.ReservedQuantity, cancellationToken).ConfigureAwait(false);

        var lowStockItems = await items
            .CountAsync(
                item => item.OnHandQuantity - item.ReservedQuantity <= lowStockThreshold,
                cancellationToken)
            .ConfigureAwait(false);

        var groups = await _context.InventoryReservations
            .AsNoTracking()
            .GroupBy(reservation => reservation.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return InventoryCountsReadModel.FromParts(
            totalItems,
            totalOnHand,
            totalReserved,
            lowStockItems,
            groups.Select(group => (group.Status, group.Count)));
    }

    private static readonly Comparer<ReservationSortKey> ReservationNewestFirstComparer =
        Comparer<ReservationSortKey>.Create(
            static (left, right) =>
            {
                var byCreated = right.CreatedAtUtc.CompareTo(left.CreatedAtUtc);
                return byCreated != 0
                    ? byCreated
                    : left.ReservationId.Value.CompareTo(right.ReservationId.Value);
            });

    private sealed record ReservationSortKey(InventoryReservationId ReservationId, DateTimeOffset CreatedAtUtc);
}
