using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.Modules.Inventory.Application.Interfaces;

namespace FluxCommerce.Modules.Inventory.Application.Expiration;

/// <summary>
/// Expiration detection and processing. Detection is a store query that only returns pending
/// reservations past their deadline (served by the (Status, ExpiresAt) index). Processing expires
/// each reservation through the Domain's <c>Expire</c> method and persists the reservation together
/// with its inventory item on a single save. The optimistic-concurrency token guarantees a
/// reservation is only ever expired once: a concurrent commit/release/expiration that already moved
/// the reservation makes the losing save fail, and the processor then simply skips that reservation.
/// </summary>
public sealed class ExpiredReservationProcessor : IExpiredReservationProcessor
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ExpiredReservationProcessor(
        IInventoryStore inventoryStore,
        IDateTimeProvider dateTimeProvider)
    {
        _inventoryStore = inventoryStore ?? throw new ArgumentNullException(nameof(inventoryStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<int> ProcessExpiredReservationsAsync(
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                "The maximum number of results must be greater than zero.");

        // One clock reading for the whole cycle so detection and every transition agree.
        var utcNow = new DateTimeOffset(_dateTimeProvider.UtcNow);

        var expiredReservationIds = await _inventoryStore
            .GetExpiredReservationIdsAsync(utcNow, maxResults, cancellationToken)
            .ConfigureAwait(false);

        var expired = 0;

        foreach (var reservationId in expiredReservationIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var reservation = await _inventoryStore
                .GetReservationByIdAsync(reservationId, cancellationToken)
                .ConfigureAwait(false);

            if (reservation is null)
                continue;

            var item = await _inventoryStore
                .GetItemBySkuAsync(reservation.Sku, cancellationToken)
                .ConfigureAwait(false);

            if (item is null)
                continue;

            // The Domain owns the expiration rules: only a Pending reservation can expire, and only
            // when its reserved quantity can actually be released. A reservation that is already
            // committed, released or expired is rejected here and never persisted again.
            var expire = reservation.Expire(item, utcNow);

            if (expire.IsFailure)
                continue;

            // The optimistic-concurrency token serializes against concurrent lifecycle operations:
            // if another operation already moved the reservation, this save is rejected and nothing
            // from this attempt is persisted. A failed reservation is skipped without affecting the
            // remaining reservations.
            if (await _inventoryStore
                    .SaveLifecycleChangesAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                expired++;
            }
        }

        return expired;
    }
}
