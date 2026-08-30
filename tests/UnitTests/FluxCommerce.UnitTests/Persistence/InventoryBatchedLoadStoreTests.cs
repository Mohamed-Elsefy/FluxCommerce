using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Stores;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Guard tests for the batched load methods on <see cref="InventoryStore"/>
/// (<see cref="InventoryStore.GetReservationsByIdsAsync"/> and
/// <see cref="InventoryStore.GetItemsBySkusAsync"/>). These back the expiration processor's N+1
/// elimination: reservations and their items are loaded in a single query each instead of one round
/// trip per reservation.
/// </summary>
public class InventoryBatchedLoadStoreTests : IDisposable
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpiresAt = new(2026, 2, 10, 8, 45, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public InventoryBatchedLoadStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task GetReservationsByIdsAsync_MultipleIds_ReturnsAllMatchingReservations()
    {
        var ids = await SeedReservedAsync(("SKU-B1", 3), ("SKU-B2", 4));

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var loaded = await store.GetReservationsByIdsAsync(ids, CancellationToken.None);

        Assert.Equal(2, loaded.Count);
        Assert.All(loaded, reservation => Assert.Contains(reservation.Id, ids));
    }

    [Fact]
    public async Task GetReservationsByIdsAsync_UnknownIds_ReturnsOnlyKnownReservations()
    {
        var ids = await SeedReservedAsync(("SKU-BK", 2));

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var unknown = InventoryReservationId.Create();

        var loaded = await store.GetReservationsByIdsAsync([ids[0], unknown], CancellationToken.None);

        var reservation = Assert.Single(loaded);
        Assert.Equal(ids[0], reservation.Id);
    }

    [Fact]
    public async Task GetReservationsByIdsAsync_EmptyCollection_ReturnsEmpty()
    {
        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var loaded = await store.GetReservationsByIdsAsync([], CancellationToken.None);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task GetItemsBySkusAsync_MultipleSkus_ReturnsAllMatchingItems()
    {
        await SeedReservedAsync(("SKU-C1", 3), ("SKU-C2", 4));

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var loaded = await store.GetItemsBySkusAsync(
            [new InventorySku("SKU-C1"), new InventorySku("SKU-C2")],
            CancellationToken.None);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(7, loaded.Single(item => item.Id == new InventorySku("SKU-C1")).AvailableQuantity);
        Assert.Equal(6, loaded.Single(item => item.Id == new InventorySku("SKU-C2")).AvailableQuantity);
    }

    [Fact]
    public async Task GetItemsBySkusAsync_SharedSkuAcrossReservations_ReturnsOncePerSku()
    {
        await SeedReservedAsync(("SKU-SHARED", 3));

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var loaded = await store.GetItemsBySkusAsync([new InventorySku("SKU-SHARED")], CancellationToken.None);

        var item = Assert.Single(loaded);
        Assert.Equal(new InventorySku("SKU-SHARED"), item.Id);
    }

    [Fact]
    public async Task GetItemsBySkusAsync_EmptyCollection_ReturnsEmpty()
    {
        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var loaded = await store.GetItemsBySkusAsync([], CancellationToken.None);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task BatchedLoads_TrackAggregates_BecauseMutationsArePersistable()
    {
        var ids = await SeedReservedAsync(("SKU-TRACK", 3));

        await using (var context = CreateContext())
        {
            var store = new InventoryStore(context);

            var reservation = (await store.GetReservationsByIdsAsync(ids, CancellationToken.None)).Single();
            var item = (await store.GetItemsBySkusAsync([reservation.Sku], CancellationToken.None)).Single();

            Assert.True(reservation.Expire(item, UtcNow).IsSuccess);

            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using (var reloadContext = CreateContext())
        {
            var reloadStore = new InventoryStore(reloadContext);
            var reloaded = await reloadStore.GetReservationByIdAsync(ids[0], CancellationToken.None);
            var reloadedItem = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-TRACK"), CancellationToken.None);

            Assert.NotNull(reloaded);
            Assert.NotNull(reloadedItem);
            Assert.Equal(InventoryReservationStatus.Expired, reloaded!.Status);
            Assert.Equal(0, reloadedItem!.ReservedQuantity);
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private async Task<List<InventoryReservationId>> SeedReservedAsync(
        params (string Sku, int Quantity)[] reservations)
    {
        await using var seedContext = CreateContext();
        var seedStore = new InventoryStore(seedContext);
        var ids = new List<InventoryReservationId>();

        foreach (var (skuValue, quantity) in reservations)
        {
            var sku = new InventorySku(skuValue);
            var item = InventoryItem.Create(sku, 10, UtcNow);
            var reservation = InventoryReservation.Reserve(
                item,
                InventoryReservationId.Create(),
                OrderId.Create(),
                quantity,
                ExpiresAt,
                UtcNow).Value;

            ids.Add(reservation.Id);
            await seedStore.AddItemAsync(item, CancellationToken.None);
            await seedStore.AddReservationAsync(reservation, CancellationToken.None);
        }

        await seedStore.SaveChangesAsync(CancellationToken.None);

        return ids;
    }

    private InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new InventoryDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        context.Database.EnsureCreated();

        return context;
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}
