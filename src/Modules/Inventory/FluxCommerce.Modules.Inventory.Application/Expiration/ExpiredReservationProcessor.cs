using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.Modules.Inventory.Application.Interfaces;

namespace FluxCommerce.Modules.Inventory.Application.Expiration;

/// <summary>
/// Expiration detection and processing. Detection is a store query that only returns pending
/// reservations past their deadline (served by the (Status, ExpiresAt) index). Processing batch-loads
/// the expired reservations and their inventory items in two queries (avoiding a per-reservation N+1
/// lookup), expires each reservation through the Domain's <c>Expire</c> method, and persists each
/// reservation together with its inventory item on one save per reservation. The optimistic-concurrency
/// token guarantees a reservation is only ever expired once: a concurrent commit/release/expiration
/// that already moved the reservation makes the losing save fail, and the processor then re-loads and
/// continues with the remaining reservations.
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

        var remainingIds = (await _inventoryStore
                .GetExpiredReservationIdsAsync(utcNow, maxResults, cancellationToken)
                .ConfigureAwait(false))
            .ToList();

        if (remainingIds.Count == 0 || cancellationToken.IsCancellationRequested)
            return 0;

        var expired = 0;

        // Detection is exact and bounded, but a conflicting save clears the DbContext change tracker,
        // so the batch is processed in a loop that re-loads only the reservations that were not yet
        // successfully persisted. In the common case this inner body runs exactly once.
        while (remainingIds.Count > 0)
        {
            // Two batched queries load the whole remaining window instead of one round trip per
            // reservation: the reservations and the (usually distinct) inventory items they need.
            var reservationsById = (await _inventoryStore
                    .GetReservationsByIdsAsync(remainingIds, cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(reservation => reservation.Id);

            var itemsById = (await _inventoryStore
                    .GetItemsBySkusAsync(
                        reservationsById.Values.Select(reservation => reservation.Sku).Distinct().ToList(),
                        cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(item => item.Id);

            var conflictIndex = -1;

            for (var index = 0; index < remainingIds.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var reservationId = remainingIds[index];

                if (!reservationsById.TryGetValue(reservationId, out var reservation))
                    continue;

                if (!itemsById.TryGetValue(reservation.Sku, out var item))
                    continue;

                // The Domain owns the expiration rules: only a Pending reservation can expire, and only
                // when its reserved quantity can actually be released. A reservation that is already
                // committed, released or expired is rejected here and never persisted again.
                if (reservation.Expire(item, utcNow).IsFailure)
                    continue;

                // The optimistic-concurrency token serializes against concurrent lifecycle operations:
                // if another operation already moved the reservation, this save is rejected and nothing
                // from this attempt is persisted. The conflict clears the change tracker, so the not-yet
                // -persisted reservations are re-loaded and the remaining reservations still succeed.
                if (await _inventoryStore
                        .SaveLifecycleChangesAsync(cancellationToken)
                        .ConfigureAwait(false))
                {
                    expired++;
                }
                else
                {
                    conflictIndex = index;
                    break;
                }
            }

            if (conflictIndex < 0)
                break;

            remainingIds = remainingIds.Skip(conflictIndex + 1).ToList();
        }

        return expired;
    }
}
