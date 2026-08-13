using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Application.Interfaces;

/// <summary>
/// Persistence driven port for the Inventory stock-management and reservation lifecycle use cases.
/// Reads load tracked aggregates so mutations can be flushed through the same DbContext; the
/// reservation and its inventory item are persisted together on <c>SaveChangesAsync</c>, keeping each
/// reservation operation atomic. Duplicate-SKU detection is enforced by the SKU primary key in the
/// database and by an existence check in the create handler.
/// </summary>
public interface IInventoryStore
{
    Task<InventoryItem?> GetItemBySkuAsync(
        InventorySku sku,
        CancellationToken cancellationToken = default);

    Task AddItemAsync(
        InventoryItem item,
        CancellationToken cancellationToken = default);

    Task<InventoryReservation?> GetReservationByIdAsync(
        InventoryReservationId reservationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the identifiers of up to <paramref name="maxResults"/> reservations that are still
    /// pending and whose expiration deadline is at or before <paramref name="utcNow"/>, ordered by
    /// deadline so the oldest reservations are expired first. Only reservations that can actually
    /// require expiration are returned; committed, released and already-expired reservations are
    /// never included.
    /// </summary>
    Task<IReadOnlyList<InventoryReservationId>> GetExpiredReservationIdsAsync(
        DateTimeOffset utcNow,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether an inventory item exists for <paramref name="sku"/>.
    /// </summary>
    Task<bool> ExistsAsync(
        InventorySku sku,
        CancellationToken cancellationToken = default);

    Task AddReservationAsync(
        InventoryReservation reservation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically reserves <paramref name="quantity"/> units of <paramref name="sku"/> and persists
    /// the created reservation in the same transaction. The stock gate is a single conditional UPDATE
    /// that only takes effect while enough quantity is actually available, so concurrent reservations
    /// can never oversell. Returns the created reservation, or <c>null</c> when the requested
    /// quantity is not available (inventory state is then unchanged).
    /// </summary>
    Task<InventoryReservation?> TryReserveAsync(
        InventorySku sku,
        InventoryReservationId reservationId,
        OrderId orderId,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes a reservation lifecycle mutation (release, commit, expiration). Returns
    /// <c>true</c> when the mutation persisted, or <c>false</c> when a concurrent operation already
    /// moved the reservation and the optimistic-concurrency check rejected the losing attempt —
    /// in that case nothing from this attempt is persisted.
    /// </summary>
    Task<bool> SaveLifecycleChangesAsync(CancellationToken cancellationToken = default);
}
