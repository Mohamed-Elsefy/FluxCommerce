using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 5.7 reservation expiration &amp; recovery semantics, run against the real SQL Server engine so
/// the (Status, ExpiresAt) detection query and the optimistic-concurrency token are exercised by the
/// actual database. The processor expires each pending, past-deadline reservation through the Domain
/// and persists it together with its inventory item on a single save.
/// </summary>
[Collection(SqlServerInventoryTestCollection.Name)]
public class InventoryReservationExpirationTests
{
    private readonly SqlServerInventoryTestFixture _fixture;

    public InventoryReservationExpirationTests(SqlServerInventoryTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PendingReservationPastDeadline_IsExpired_AndStockReleased()
    {
        var sku = UniqueSku("EXP-PAST");
        var reservationId = await _fixture.SeedReservationAsync(sku, onHand: 10, quantity: 3, PastDeadline());

        var expired = await _fixture.RunExpirationProcessorAsync(maxResults: 100);

        Assert.True(expired >= 1,
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        var reservation = reservations.Single(candidate => candidate.Id.Value == reservationId);
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
    }

    [Fact]
    public async Task PendingReservationBeforeDeadline_IsNotExpired()
    {
        var sku = UniqueSku("EXP-FUTURE");
        var reservationId = await _fixture.SeedReservationAsync(sku, onHand: 10, quantity: 3, FutureDeadline());

        var expired = await _fixture.RunExpirationProcessorAsync(maxResults: 100);

        Assert.Equal(0, expired);

        var item = await _fixture.GetItemAsync(sku);
        Assert.Equal(3, item.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        var reservation = reservations.Single(candidate => candidate.Id.Value == reservationId);
        Assert.Equal(InventoryReservationStatus.Pending, reservation.Status);
    }

    [Fact]
    public async Task AlreadyExpiredReservation_IsNotExpiredAgain()
    {
        var sku = UniqueSku("EXP-ALREADY");
        var reservationId = await _fixture.SeedReservationAsync(
            sku,
            onHand: 10,
            quantity: 3,
            PastDeadline(),
            InventoryReservationStatus.Expired);

        var expired = await _fixture.RunExpirationProcessorAsync(maxResults: 100);

        Assert.Equal(0, expired);

        var item = await _fixture.GetItemAsync(sku);
        Assert.Equal(0, item.ReservedQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        var reservation = reservations.Single(candidate => candidate.Id.Value == reservationId);
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
    }

    [Fact]
    public async Task CommittedReservation_PastDeadline_IsNotReleased()
    {
        var sku = UniqueSku("EXP-COMMITTED");
        var reservationId = await _fixture.SeedReservationAsync(
            sku,
            onHand: 10,
            quantity: 3,
            PastDeadline(),
            InventoryReservationStatus.Committed);

        var expired = await _fixture.RunExpirationProcessorAsync(maxResults: 100);

        Assert.Equal(0, expired);

        var item = await _fixture.GetItemAsync(sku);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(7, item.OnHandQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        var reservation = reservations.Single(candidate => candidate.Id.Value == reservationId);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
    }

    [Fact]
    public async Task RepeatedCycles_AreIdempotent()
    {
        var sku = UniqueSku("EXP-IDEMPOTENT");
        await _fixture.SeedReservationAsync(sku, onHand: 10, quantity: 3, PastDeadline());

        var first = await _fixture.RunExpirationProcessorAsync(maxResults: 100);
        var second = await _fixture.RunExpirationProcessorAsync(maxResults: 100);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task MultipleExpiredReservations_AllProcessed()
    {
        var skuA = UniqueSku("EXP-A");
        var skuB = UniqueSku("EXP-B");
        var skuC = UniqueSku("EXP-C");
        await _fixture.SeedReservationAsync(skuA, onHand: 10, quantity: 3, PastDeadline().AddMinutes(-2));
        await _fixture.SeedReservationAsync(skuB, onHand: 10, quantity: 3, PastDeadline().AddMinutes(-1));
        await _fixture.SeedReservationAsync(skuC, onHand: 10, quantity: 3, PastDeadline());

        var expired = await _fixture.RunExpirationProcessorAsync(maxResults: 100);

        Assert.True(expired >= 3,
            var item = await _fixture.GetItemAsync(sku);
            Assert.Equal(0, item.ReservedQuantity);
        }
    }

    [Fact]
    public async Task BatchSize_LimitsNumberOfExpiredReservations()
    {
        var skuA = UniqueSku("EXP-BA");
        var skuB = UniqueSku("EXP-BB");
        var skuC = UniqueSku("EXP-BC");
        await _fixture.SeedReservationAsync(skuA, onHand: 10, quantity: 1, PastDeadline());
        await _fixture.SeedReservationAsync(skuB, onHand: 10, quantity: 1, PastDeadline());
        await _fixture.SeedReservationAsync(skuC, onHand: 10, quantity: 1, PastDeadline());

        var firstBatch = await _fixture.RunExpirationProcessorAsync(maxResults: 2);
        var secondBatch = await _fixture.RunExpirationProcessorAsync(maxResults: 2);

        Assert.Equal(2, firstBatch);
        Assert.Equal(1, secondBatch);
    }

    [Fact]
    public async Task ConcurrentProcessorRuns_ExpireExactlyOnce()
    {
        var sku = UniqueSku("EXP-CONCURRENT");
        await _fixture.SeedReservationAsync(sku, onHand: 10, quantity: 3, PastDeadline());

        var results = await Task.WhenAll(
            _fixture.RunExpirationProcessorAsync(100),
            _fixture.RunExpirationProcessorAsync(100));

        // The optimistic-concurrency token guarantees the reservation is expired exactly once even
        // when two processor instances race on the same expiration.
        Assert.Equal(1, results[0] + results[1]);

        var item = await _fixture.GetItemAsync(sku);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        var reservation = reservations.Single(candidate => candidate.Sku.Value == sku.ToUpperInvariant());
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
    }

    private static DateTimeOffset PastDeadline() => DateTimeOffset.UtcNow.AddMinutes(-5);

    private static DateTimeOffset FutureDeadline() => DateTimeOffset.UtcNow.AddMinutes(30);

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
}
