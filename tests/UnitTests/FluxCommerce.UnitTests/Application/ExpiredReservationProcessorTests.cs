using FluxCommerce.Modules.Inventory.Application.Expiration;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.UnitTests.TestInfrastructure;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ExpiredReservationProcessorTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 7, 10, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PastDeadline = UtcNow.AddMinutes(-5);

    [Fact]
    public async Task ProcessExpiredReservationsAsync_NoExpiredReservations_ReturnsZero_AndPersistsNothing()
    {
        var store = CreateStore();
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(0, expired);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_ExpiredReservation_ExpiresAndReleasesQuantity()
    {
        var (item, reservation) = CreatePendingState(PastDeadline);
        var store = CreateStore((item, reservation));
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(1, expired);
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_ReservationNotPastDeadline_IsNotReturnedByDetection()
    {
        // The store query is the deadline gate: a reservation whose deadline has not passed is simply
        // never returned, so nothing is expired and nothing is persisted.
        var (item, reservation) = CreatePendingState(UtcNow.AddMinutes(15));
        var store = CreateStore((item, reservation));
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(InventoryReservationStatus.Pending, reservation.Status);
        Assert.Equal(3, item.ReservedQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_CommittedReservation_IsNotReleased()
    {
        var (item, reservation) = CreateState(InventoryReservationStatus.Committed, PastDeadline);
        var store = CreateStore((item, reservation));
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_AlreadyReleasedReservation_IsNotExpiredAgain()
    {
        var (item, reservation) = CreateState(InventoryReservationStatus.Released, PastDeadline);
        var store = CreateStore((item, reservation));
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_AlreadyExpiredReservation_IsNotExpiredAgain()
    {
        var (item, reservation) = CreateState(InventoryReservationStatus.Expired, PastDeadline);
        var store = CreateStore((item, reservation));
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_RepeatedProcessing_IsSafe()
    {
        var (item, reservation) = CreatePendingState(PastDeadline);
        var store = CreateStore((item, reservation));
        var processor = CreateProcessor(store);

        // First cycle expires the reservation. The store query is then empty because the reservation
        // is no longer pending, so a second cycle has nothing to do.
        var first = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);
        var second = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_MultipleExpiredReservations_ProcessesAll()
    {
        var (itemA, reservationA) = CreatePendingState("SKU-A", PastDeadline.AddMinutes(-2));
        var (itemB, reservationB) = CreatePendingState("SKU-B", PastDeadline.AddMinutes(-1));
        var (itemC, reservationC) = CreatePendingState("SKU-C", PastDeadline);

        var store = CreateStore((itemA, reservationA), (itemB, reservationB), (itemC, reservationC));
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(3, expired);
        Assert.Equal(InventoryReservationStatus.Expired, reservationA.Status);
        Assert.Equal(InventoryReservationStatus.Expired, reservationB.Status);
        Assert.Equal(InventoryReservationStatus.Expired, reservationC.Status);
        Assert.All(new[] { itemA, itemB, itemC }, item => Assert.Equal(0, item.ReservedQuantity));
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_ConcurrentOperationWon_SkipsWithoutCounting()
    {
        var (item, reservation) = CreatePendingState(PastDeadline);
        var store = CreateStore((item, reservation));
        store
            .Setup(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        // The optimistic-concurrency check rejected the save because another operation already moved
        // the reservation; the losing attempt must not be reported as an expiration.
        Assert.Equal(0, expired);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_ConflictingReservation_DoesNotPreventRemainingOnes()
    {
        var (itemA, reservationA) = CreatePendingState("SKU-A", PastDeadline.AddMinutes(-2));
        var (itemB, reservationB) = CreatePendingState("SKU-B", PastDeadline.AddMinutes(-1));
        var (itemC, reservationC) = CreatePendingState("SKU-C", PastDeadline);

        var store = CreateStore((itemA, reservationA), (itemB, reservationB), (itemC, reservationC));
        store
            .SetupSequence(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)   // A expires
            .ReturnsAsync(false)  // B loses a concurrent race
            .ReturnsAsync(true);  // C is still processed after the conflict re-load
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        // The conflicting reservation is not counted, but the sibling after it is still expired.
        Assert.Equal(2, expired);
        Assert.Equal(InventoryReservationStatus.Expired, reservationA.Status);
        Assert.Equal(InventoryReservationStatus.Expired, reservationC.Status);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_MissingReservation_Skips()
    {
        var (_, reservation) = CreatePendingState(PastDeadline);
        var store = CreateStore();
        store
            .Setup(s => s.GetExpiredReservationIdsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([reservation.Id]);
        store
            .Setup(s => s.GetReservationsByIdsAsync(It.IsAny<IReadOnlyCollection<InventoryReservationId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InventoryReservation>());
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(0, expired);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_MissingItem_SkipsWithoutPersisting()
    {
        var (_, reservation) = CreatePendingState(PastDeadline);
        var store = CreateStore();
        store
            .Setup(s => s.GetExpiredReservationIdsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([reservation.Id]);
        store
            .Setup(s => s.GetReservationsByIdsAsync(It.IsAny<IReadOnlyCollection<InventoryReservationId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([reservation]);
        store
            .Setup(s => s.GetItemsBySkusAsync(It.IsAny<IReadOnlyCollection<InventorySku>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InventoryItem>());
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(InventoryReservationStatus.Pending, reservation.Status);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_InconsistentReservedQuantity_DoesNotReleaseBeyondReserved()
    {
        // Recovery scenario: the item row does not reserve enough quantity to cover the reservation.
        // The Domain rejects the release, so nothing is persisted and the state cannot be corrupted
        // further by expiring.
        var item = InventoryItem.Create(new InventorySku("SKU-1"), 10, UtcNow);
        item.Reserve(1, UtcNow);
        var reservation = InventoryReservation.Hydrate(
            InventoryReservationId.Create(),
            OrderId.Create(),
            item.Sku,
            3,
            InventoryReservationStatus.Pending,
            PastDeadline,
            InventoryReservation.InitialVersion,
            UtcNow,
            UtcNow);

        var store = CreateStore((item, reservation));
        var processor = CreateProcessor(store);

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(InventoryReservationStatus.Pending, reservation.Status);
        Assert.Equal(1, item.ReservedQuantity);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_CancellationRequested_StopsProcessing()
    {
        var (itemA, reservationA) = CreatePendingState(PastDeadline.AddMinutes(-2));
        var (itemB, reservationB) = CreatePendingState(PastDeadline.AddMinutes(-1));

        var store = CreateStore((itemA, reservationA), (itemB, reservationB));
        var processor = CreateProcessor(store);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var expired = await processor.ProcessExpiredReservationsAsync(maxResults: 100, cts.Token);

        Assert.Equal(0, expired);
        store.Verify(s => s.GetReservationsByIdsAsync(It.IsAny<IReadOnlyCollection<InventoryReservationId>>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_ZeroMaxResults_Throws()
    {
        var store = CreateStore();
        var processor = CreateProcessor(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => processor.ProcessExpiredReservationsAsync(maxResults: 0, CancellationToken.None));
    }

    private static ExpiredReservationProcessor CreateProcessor(Mock<IInventoryStore> store) =>
        new(store.Object, FixedDateTimeProvider.CreateDefault());

    private static (InventoryItem Item, InventoryReservation Reservation) CreatePendingState(DateTimeOffset expiresAt) =>
        CreateState("SKU-1", InventoryReservationStatus.Pending, expiresAt);

    private static (InventoryItem Item, InventoryReservation Reservation) CreatePendingState(string sku, DateTimeOffset expiresAt) =>
        CreateState(sku, InventoryReservationStatus.Pending, expiresAt);

    private static (InventoryItem Item, InventoryReservation Reservation) CreateState(
        InventoryReservationStatus status,
        DateTimeOffset expiresAt) =>
        CreateState("SKU-1", status, expiresAt);

    /// <summary>
    /// Builds an item whose quantities are consistent with the reservation's terminal status, so each
    /// scenario reflects the real persisted state the processor would observe.
    /// </summary>
    private static (InventoryItem Item, InventoryReservation Reservation) CreateState(
        string sku,
        InventoryReservationStatus status,
        DateTimeOffset expiresAt)
    {
        var item = InventoryItem.Create(new InventorySku(sku), 10, UtcNow);

        switch (status)
        {
            case InventoryReservationStatus.Committed:
                item.Reserve(3, UtcNow);
                item.Commit(3, UtcNow);
                break;
            case InventoryReservationStatus.Released:
            case InventoryReservationStatus.Expired:
                item.Reserve(3, UtcNow);
                item.Release(3, UtcNow);
                break;
            case InventoryReservationStatus.Pending:
                item.Reserve(3, UtcNow);
                break;
        }

        var reservation = InventoryReservation.Hydrate(
            InventoryReservationId.Create(),
            OrderId.Create(),
            item.Sku,
            3,
            status,
            expiresAt,
            InventoryReservation.InitialVersion,
            UtcNow,
            UtcNow);

        return (item, reservation);
    }

    /// <summary>
    /// Builds a store whose detection query only returns reservations that are still pending and past
    /// their deadline — the same filter the real store applies — so repeated-processing and deadline
    /// tests behave like the database.
    /// </summary>
    private static Mock<IInventoryStore> CreateStore(params (InventoryItem Item, InventoryReservation Reservation)[] state)
    {
        var reservations = state.Select(pair => pair.Reservation).ToArray();
        var items = state.Select(pair => pair.Item).ToArray();

        var store = new Mock<IInventoryStore>();
        store
            .Setup(s => s.GetExpiredReservationIdsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset utcNow, int maxResults, CancellationToken _) => reservations
                .Where(reservation => reservation.Status == InventoryReservationStatus.Pending
                    && reservation.ExpiresAt <= utcNow)
                .Select(reservation => reservation.Id)
                .Take(maxResults)
                .ToList());
        store
            .Setup(s => s.GetReservationsByIdsAsync(It.IsAny<IReadOnlyCollection<InventoryReservationId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<InventoryReservationId> ids, CancellationToken _) =>
                reservations.Where(reservation => ids.Contains(reservation.Id)).ToList());
        store
            .Setup(s => s.GetItemsBySkusAsync(It.IsAny<IReadOnlyCollection<InventorySku>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<InventorySku> skus, CancellationToken _) =>
                items.Where(item => skus.Contains(item.Id)).ToList());
        store
            .Setup(s => s.SaveLifecycleChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return store;
    }
}
