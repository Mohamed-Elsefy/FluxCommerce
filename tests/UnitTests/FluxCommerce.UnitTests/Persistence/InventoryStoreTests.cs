using FluxCommerce.Modules.Inventory.Application.Interfaces;
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

public class InventoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpiresAt = UtcNow.AddMinutes(30);

    private readonly SqliteConnection _connection;

    public InventoryStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task AddItemAsync_ThenGetItemBySkuAsync_RoundTrips()
    {
        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var item = InventoryItem.Create(new InventorySku("SKU-STORE"), 10, UtcNow);

        await store.AddItemAsync(item, CancellationToken.None);
        await store.SaveChangesAsync(CancellationToken.None);

        var loaded = await store.GetItemBySkuAsync(new InventorySku("SKU-STORE"), CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(new InventorySku("SKU-STORE"), loaded!.Id);
        Assert.Equal(10, loaded.OnHandQuantity);
        Assert.Equal(0, loaded.ReservedQuantity);
        Assert.Equal(10, loaded.AvailableQuantity);
        Assert.Equal(InventoryItem.InitialVersion, loaded.Version);
    }

    [Fact]
    public async Task GetItemBySkuAsync_UnknownSku_ReturnsNull()
    {
        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var loaded = await store.GetItemBySkuAsync(new InventorySku("SKU-MISSING"), CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetItemBySkuAsync_ReturnsTrackedAggregate_ThatCanBeMutatedAndSaved()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await seedStore.AddItemAsync(InventoryItem.Create(new InventorySku("SKU-TRACK"), 10, UtcNow), CancellationToken.None);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var loaded = await store.GetItemBySkuAsync(new InventorySku("SKU-TRACK"), CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(loaded!.AddStock(5, UtcNow).IsSuccess);

        await store.SaveChangesAsync(CancellationToken.None);

        var reloaded = await store.GetItemBySkuAsync(new InventorySku("SKU-TRACK"), CancellationToken.None);
        Assert.Equal(15, reloaded!.OnHandQuantity);
        Assert.Equal(2, reloaded.Version);
    }

    [Fact]
    public async Task AddItemAsync_DuplicateSku_ThrowsDbUpdateException()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await seedStore.AddItemAsync(InventoryItem.Create(new InventorySku("SKU-DUP"), 5, UtcNow), CancellationToken.None);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        await store.AddItemAsync(InventoryItem.Create(new InventorySku("SKU-DUP"), 5, UtcNow), CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveChangesAsync(CancellationToken.None));
    }

    // ---- Atomic conditional reservation ----

    [Fact]
    public async Task TryReserveAsync_SufficientStock_ReservesAtomicallyAndPersistsReservation()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await seedStore.AddItemAsync(InventoryItem.Create(new InventorySku("SKU-ATOMIC"), 10, UtcNow), CancellationToken.None);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var reservation = await store.TryReserveAsync(
            new InventorySku("SKU-ATOMIC"),
            InventoryReservationId.Create(),
            OrderId.Create(),
            3,
            ExpiresAt,
            UtcNow,
            CancellationToken.None);

        Assert.NotNull(reservation);
        Assert.Equal(new InventorySku("SKU-ATOMIC"), reservation!.Sku);
        Assert.Equal(3, reservation.Quantity);
        Assert.Equal(InventoryReservationStatus.Pending, reservation.Status);

        await using var reloadContext = CreateContext();
        var reloadStore = new InventoryStore(reloadContext);
        var item = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-ATOMIC"), CancellationToken.None);
        Assert.Equal(3, item!.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);
        Assert.Equal(2, item.Version);

        var persisted = await reloadStore.GetReservationByIdAsync(reservation.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(3, persisted!.Quantity);
    }

    [Fact]
    public async Task TryReserveAsync_InsufficientStock_ReturnsNullAndLeavesInventoryUnchanged()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await seedStore.AddItemAsync(InventoryItem.Create(new InventorySku("SKU-LOW"), 2, UtcNow), CancellationToken.None);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var reservation = await store.TryReserveAsync(
            new InventorySku("SKU-LOW"),
            InventoryReservationId.Create(),
            OrderId.Create(),
            5,
            ExpiresAt,
            UtcNow,
            CancellationToken.None);

        Assert.Null(reservation);

        await using var reloadContext = CreateContext();
        var reloadStore = new InventoryStore(reloadContext);
        var item = await reloadStore.GetItemBySkuAsync(new InventorySku("SKU-LOW"), CancellationToken.None);
        Assert.Equal(0, item!.ReservedQuantity);
        Assert.Equal(2, item.AvailableQuantity);
        Assert.Equal(InventoryItem.InitialVersion, item.Version);
        Assert.Equal(0, reloadContext.InventoryReservations.Count());
    }

    [Fact]
    public async Task TryReserveAsync_UnknownSku_ReturnsNull()
    {
        await using var context = CreateContext();
        var store = new InventoryStore(context);

        var reservation = await store.TryReserveAsync(
            new InventorySku("SKU-MISSING"),
            InventoryReservationId.Create(),
            OrderId.Create(),
            1,
            ExpiresAt,
            UtcNow,
            CancellationToken.None);

        Assert.Null(reservation);
        Assert.Equal(0, context.InventoryReservations.Count());
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForExistingSku_AndFalseForUnknownSku()
    {
        await using (var seedContext = CreateContext())
        {
            var seedStore = new InventoryStore(seedContext);
            await seedStore.AddItemAsync(InventoryItem.Create(new InventorySku("SKU-EXISTS"), 5, UtcNow), CancellationToken.None);
            await seedStore.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext();
        var store = new InventoryStore(context);

        Assert.True(await store.ExistsAsync(new InventorySku("SKU-EXISTS"), CancellationToken.None));
        Assert.False(await store.ExistsAsync(new InventorySku("SKU-UNKNOWN"), CancellationToken.None));
    }

    public void Dispose()
    {
        _connection.Dispose();
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
