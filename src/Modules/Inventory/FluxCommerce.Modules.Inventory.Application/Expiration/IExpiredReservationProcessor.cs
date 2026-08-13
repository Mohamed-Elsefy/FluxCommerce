namespace FluxCommerce.Modules.Inventory.Application.Expiration;

/// <summary>
/// Application use case that detects pending reservations whose expiration deadline has passed and
/// expires them through the Domain. Each reservation is processed independently: an already-terminal
/// reservation is skipped, and a concurrent lifecycle operation that wins the optimistic-concurrency
/// race causes the losing attempt to be skipped — so a reservation can never release its quantity
/// more than once, and a failed individual reservation cannot corrupt unrelated inventory state.
/// </summary>
public interface IExpiredReservationProcessor
{
    /// <summary>
    /// Expires up to <paramref name="maxResults"/> expired pending reservations and returns the
    /// number actually expired. Repeated invocation is safe: reservations that were already expired,
    /// released or committed are never touched again.
    /// </summary>
    Task<int> ProcessExpiredReservationsAsync(
        int maxResults,
        CancellationToken cancellationToken = default);
}
