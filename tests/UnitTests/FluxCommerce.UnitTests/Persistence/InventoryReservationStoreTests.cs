using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.Errors;
using FluxCommerce.Modules.Inventory.Domain.Results;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Stores;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

public class InventoryReservationStoreTests : IDisposable
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpiresAt = new(2026, 2, 10, 8, 45, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public InventoryReservationStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task AddReservationAsync_ThenGetReservationByIdAsync_RoundTrips()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await SeedItemAsync(seedStore, new InventorySku("SKU-RT"), 10);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var item = await store.GetItemBySkuAsync(new InventorySku("SKU-RT"), CancellationToken.None);
        var reservationId = InventoryReservationId.Create();
        var orderId = OrderId.Create();
        var reservation = InventoryReservation.Reserve(item!, reservationId, orderId, 3, ExpiresAt, UtcNow).Value;

        await store.AddReservationAsync(reservation, CancellationToken.None);
        await store.SaveChangesAsync(CancellationToken.None);

        var loaded = await store.GetReservationByIdAsync(reservationId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(reservationId, loaded!.Id);
        Assert.Equal(orderId, loaded.OrderId);
        Assert.Equal(new InventorySku("SKU-RT"), loaded.Sku);
        Assert.Equal(3, loaded.Quantity);
        Assert.Equal(InventoryReservationStatus.Pending, loaded.Status);
        Assert.Equal(ExpiresAt, loaded.ExpiresAt);
        Assert.Equal(InventoryReservation.InitialVersion, loaded.Version);
        Assert.Equal(UtcNow, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task GetReservationByIdAsync_UnknownReservation_ReturnsNull()
    {
        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var loaded = await store.GetReservationByIdAsync(
            InventoryReservationId.Create(),
            CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Reserve_PersistsReservationAndInventory_Atomically()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await SeedItemAsync(seedStore, new InventorySku("SKU-ATOMIC"), 10);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var store = new InventoryStore(context);

            var item = await store.GetItemBySkuAsync(new InventorySku("SKU-ATOMIC"), CancellationToken.None);
            var reservation = InventoryReservation.Reserve(
                item!,
                InventoryReservationId.Create(),
                OrderId.Create(),
                3,
                ExpiresAt,
                UtcNow).Value;

            await store.AddReservationAsync(reservation, CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using var reloadContext = CreateContext();
        var reloadStore = new InventoryStore(reloadContext);

        var reloadedItem = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-ATOMIC"), CancellationToken.None);
        var reservations = await reloadContext.InventoryReservations
            .Where(reservation => reservation.Sku == new InventorySku("SKU-ATOMIC"))
            .ToListAsync(CancellationToken.None);

        Assert.Equal(3, reloadedItem!.ReservedQuantity);
        Assert.Equal(7, reloadedItem.AvailableQuantity);
        Assert.Single(reservations);
        Assert.Equal(InventoryReservationStatus.Pending, reservations[0].Status);
    }

    [Fact]
    public async Task FailedReserve_LeavesNoPartialStateInDatabase()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await SeedItemAsync(seedStore, new InventorySku("SKU-FAIL"), 1);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var store = new InventoryStore(context);

            var item = await store.GetItemBySkuAsync(new InventorySku("SKU-FAIL"), CancellationToken.None);
            var reserveResult = InventoryReservation.Reserve(
                item!,
                InventoryReservationId.Create(),
                OrderId.Create(),
                2,
                ExpiresAt,
                UtcNow);

            Assert.True(reserveResult.IsFailure);
            Assert.Equal(InventoryErrors.InsufficientStock.Code, reserveResult.Error.Code);

            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using var reloadContext = CreateContext();
        var reloadStore = new InventoryStore(reloadContext);

        var reloadedItem = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-FAIL"), CancellationToken.None);
        var reservationCount = await reloadContext.InventoryReservations.CountAsync(CancellationToken.None);

        Assert.Equal(0, reloadedItem!.ReservedQuantity);
        Assert.Equal(1, reloadedItem.AvailableQuantity);
        Assert.Equal(0, reservationCount);
    }

    [Fact]
    public async Task MultipleReservationsForSameSku_PersistBothAndAccumulateReservedQuantity()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await SeedItemAsync(seedStore, new InventorySku("SKU-MULTI"), 10);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var store = new InventoryStore(context);

            var item = await store.GetItemBySkuAsync(new InventorySku("SKU-MULTI"), CancellationToken.None);

            var first = InventoryReservation.Reserve(
                item!,
                InventoryReservationId.Create(),
                OrderId.Create(),
                3,
                ExpiresAt,
                UtcNow).Value;
            var second = InventoryReservation.Reserve(
                item!,
                InventoryReservationId.Create(),
                OrderId.Create(),
                4,
                ExpiresAt,
                UtcNow).Value;

            await store.AddReservationAsync(first, CancellationToken.None);
            await store.AddReservationAsync(second, CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using var reloadContext = CreateContext();
        var reloadStore = new InventoryStore(reloadContext);

        var reloadedItem = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-MULTI"), CancellationToken.None);
        var reservations = await reloadContext.InventoryReservations
            .Where(reservation => reservation.Sku == new InventorySku("SKU-MULTI"))
            .ToListAsync(CancellationToken.None);

        Assert.Equal(2, reservations.Count);
        Assert.Equal(7, reloadedItem!.ReservedQuantity);
        Assert.Equal(3, reloadedItem.AvailableQuantity);
    }

    [Fact]
    public async Task Release_RoundTrips_ThroughStore()
    {
        var reservation = await SeedReservedItemAsync("SKU-REL", 10, 3);

        await using (var context = CreateContext())
        {
            var store = new InventoryStore(context);

            var trackedReservation = await store.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
            var trackedItem = await store.GetItemBySkuAsync(new InventorySku("SKU-REL"), CancellationToken.None);

            Assert.True(trackedReservation!.Release(trackedItem!, UtcNow).IsSuccess);

            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using var reloadContext = CreateContext();
        var reloadStore = new InventoryStore(reloadContext);

        var reloadedReservation = await reloadStore.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
        var reloadedItem = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-REL"), CancellationToken.None);

        Assert.Equal(InventoryReservationStatus.Released, reloadedReservation!.Status);
        Assert.Equal(0, reloadedItem!.ReservedQuantity);
        Assert.Equal(10, reloadedItem.AvailableQuantity);
    }

    [Fact]
    public async Task Commit_RoundTrips_ThroughStore()
    {
        var reservation = await SeedReservedItemAsync("SKU-COM", 10, 3);

        await using (var context = CreateContext())
        {
            var store = new InventoryStore(context);

            var trackedReservation = await store.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
            var trackedItem = await store.GetItemBySkuAsync(new InventorySku("SKU-COM"), CancellationToken.None);

            Assert.True(trackedReservation!.Commit(trackedItem!, UtcNow).IsSuccess);

            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using var reloadContext = CreateContext();
        var reloadStore = new InventoryStore(reloadContext);

        var reloadedReservation = await reloadStore.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
        var reloadedItem = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-COM"), CancellationToken.None);

        Assert.Equal(InventoryReservationStatus.Committed, reloadedReservation!.Status);
        Assert.Equal(7, reloadedItem!.OnHandQuantity);
        Assert.Equal(0, reloadedItem.ReservedQuantity);
        Assert.Equal(7, reloadedItem.AvailableQuantity);
    }

    [Fact]
    public async Task Expire_RoundTrips_ThroughStore()
    {
        var reservation = await SeedReservedItemAsync("SKU-EXP", 10, 3);

        await using (var context = CreateContext())
        {
            var store = new InventoryStore(context);

            var trackedReservation = await store.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
            var trackedItem = await store.GetItemBySkuAsync(new InventorySku("SKU-EXP"), CancellationToken.None);

            Assert.True(trackedReservation!.Expire(trackedItem!, UtcNow).IsSuccess);

            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using var reloadContext = CreateContext();
        var reloadStore = new InventoryStore(reloadContext);

        var reloadedReservation = await reloadStore.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
        var reloadedItem = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-EXP"), CancellationToken.None);

        Assert.Equal(InventoryReservationStatus.Expired, reloadedReservation!.Status);
        Assert.Equal(0, reloadedItem!.ReservedQuantity);
        Assert.Equal(10, reloadedItem.AvailableQuantity);
    }

    [Fact]
    public async Task RepeatedRelease_IsRejected_WithoutDoubleRelease()
    {
        var reservation = await SeedReservedItemAsync("SKU-REPEAT", 10, 3);

        await using (var context = CreateContext())
        {
            var store = new InventoryStore(context);

            var firstReservation = await store.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
            var item = await store.GetItemBySkuAsync(new InventorySku("SKU-REPEAT"), CancellationToken.None);

            Assert.True(firstReservation!.Release(item!, UtcNow).IsSuccess);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using var secondContext = CreateContext();
        var secondStore = new InventoryStore(secondContext);

        var secondReservation = await secondStore.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
        var reloadedItem = await secondStore.GetItemBySkuAsync(new InventorySku("SKU-REPEAT"), CancellationToken.None);

        var repeated = secondReservation!.Release(reloadedItem!, UtcNow);

        Assert.True(repeated.IsFailure);
        Assert.Equal(InventoryErrors.InvalidStateTransition.Code, repeated.Error.Code);

        await secondStore.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0, reloadedItem!.ReservedQuantity);
        Assert.Equal(10, reloadedItem.AvailableQuantity);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private async Task<InventoryReservation> SeedReservedItemAsync(string skuValue, int onHand, int quantity)
    {
        var sku = new InventorySku(skuValue);

        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);

            var item = InventoryItem.Create(sku, onHand, UtcNow);
            var reservation = InventoryReservation.Reserve(
                item,
                InventoryReservationId.Create(),
                OrderId.Create(),
                quantity,
                ExpiresAt,
                UtcNow).Value;

            await seedStore.AddItemAsync(item, CancellationToken.None);
            await seedStore.AddReservationAsync(reservation, CancellationToken.None);
            await seedStore.SaveChangesAsync(CancellationToken.None);

            return reservation;
        }
    }

    private static Task SeedItemAsync(InventoryStore store, InventorySku sku, int onHand)
    {
        store.AddItemAsync(InventoryItem.Create(sku, onHand, UtcNow), CancellationToken.None);
        return Task.CompletedTask;
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
