using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Errors;
using FluxCommerce.Modules.Inventory.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class InventoryItemTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

    private static InventoryItem CreateItem(int onHandQuantity = 10) =>
        InventoryItem.Create(new InventorySku("SKU-1"), onHandQuantity, UtcNow);

    // ---- Creation ----

    [Fact]
    public void Create_SetsInitialStockState()
    {
        var item = CreateItem(onHandQuantity: 10);

        Assert.Equal(10, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void Create_WithZeroOnHand_IsAllowed()
    {
        var item = CreateItem(onHandQuantity: 0);

        Assert.Equal(0, item.OnHandQuantity);
        Assert.Equal(0, item.AvailableQuantity);
    }

    [Fact]
    public void Create_InitializesInitialVersionAndTimestamps()
    {
        var item = CreateItem();

        Assert.Equal(InventoryItem.InitialVersion, item.Version);
        Assert.Equal(UtcNow, item.CreatedAtUtc);
        Assert.Equal(UtcNow, item.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithNullSku_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => InventoryItem.Create(null!, 10, UtcNow));
    }

    [Fact]
    public void Create_WithNegativeOnHand_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryItem.Create(new InventorySku("SKU-1"), -1, UtcNow));
    }

    [Fact]
    public void Create_WithDefaultTimestamp_Throws()
    {
        Assert.Throws<ArgumentException>(() => InventoryItem.Create(new InventorySku("SKU-1"), 10, default));
    }

    [Fact]
    public void Create_RaisesInventoryItemCreated()
    {
        var item = CreateItem();

        var domainEvent = Assert.Single(item.GetDomainEvents().OfType<InventoryItemCreated>());
        Assert.Equal(item.Sku, domainEvent.Sku);
        Assert.Equal(10, domainEvent.OnHandQuantity);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    // ---- Core invariants ----

    [Fact]
    public void AvailableQuantity_IsOnHandMinusReserved()
    {
        var item = CreateItem(onHandQuantity: 10);
        item.Reserve(3, UtcNow);

        Assert.Equal(7, item.AvailableQuantity);
        Assert.Equal(10, item.OnHandQuantity);
        Assert.Equal(3, item.ReservedQuantity);
    }

    // ---- AddStock ----

    [Fact]
    public void AddStock_WithValidQuantity_IncreasesOnHand()
    {
        var item = CreateItem(onHandQuantity: 5);

        var result = item.AddStock(5, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, item.OnHandQuantity);
        Assert.Equal(2, item.Version);
    }

    [Fact]
    public void AddStock_WithInvalidQuantity_Fails()
    {
        var item = CreateItem();

        var result = item.AddStock(0, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidQuantity, result.Error);
        Assert.Equal(10, item.OnHandQuantity);
        Assert.Equal(InventoryItem.InitialVersion, item.Version);
    }

    [Fact]
    public void AddStock_RaisesInventoryStockAdded()
    {
        var item = CreateItem();
        item.ClearDomainEvents();

        item.AddStock(5, UtcNow);

        var domainEvent = Assert.Single(item.GetDomainEvents().OfType<InventoryStockAdded>());
        Assert.Equal(item.Sku, domainEvent.Sku);
        Assert.Equal(5, domainEvent.Quantity);
        Assert.Equal(15, domainEvent.OnHandQuantity);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    // ---- Reserve ----

    [Fact]
    public void Reserve_WithSufficientStock_ReservesQuantity()
    {
        var item = CreateItem(onHandQuantity: 10);

        var result = item.Reserve(3, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, item.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);
        Assert.Equal(2, item.Version);
    }

    [Fact]
    public void Reserve_WithInsufficientStock_Fails()
    {
        var item = CreateItem(onHandQuantity: 3);

        var result = item.Reserve(4, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientStock, result.Error);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(3, item.AvailableQuantity);
        Assert.Equal(InventoryItem.InitialVersion, item.Version);
    }

    [Fact]
    public void Reserve_WithInvalidQuantity_Fails()
    {
        var item = CreateItem();

        var result = item.Reserve(0, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidQuantity, result.Error);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Reserve_CompetingReservations_CannotExceedAvailable()
    {
        var item = CreateItem(onHandQuantity: 1);

        Assert.True(item.Reserve(1, UtcNow).IsSuccess);
        Assert.True(item.Reserve(1, UtcNow).IsFailure);

        Assert.Equal(1, item.ReservedQuantity);
        Assert.Equal(0, item.AvailableQuantity);
    }

    [Fact]
    public void Reserve_ExactAvailable_IsAllowed()
    {
        var item = CreateItem(onHandQuantity: 5);

        var result = item.Reserve(5, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, item.AvailableQuantity);
    }

    // ---- Release ----

    [Fact]
    public void Release_WithReservedQuantity_ReleasesStock()
    {
        var item = CreateItem(onHandQuantity: 10);
        item.Reserve(3, UtcNow);

        var result = item.Release(3, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void Release_ExceedingReserved_Fails()
    {
        var item = CreateItem(onHandQuantity: 10);
        item.Reserve(2, UtcNow);

        var result = item.Release(3, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.ReleaseExceedsReserved, result.Error);
        Assert.Equal(2, item.ReservedQuantity);
    }

    [Fact]
    public void Release_WithInvalidQuantity_Fails()
    {
        var item = CreateItem(onHandQuantity: 10);
        item.Reserve(2, UtcNow);

        var result = item.Release(0, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidQuantity, result.Error);
    }

    // ---- Commit ----

    [Fact]
    public void Commit_ConsumesReservedStock()
    {
        var item = CreateItem(onHandQuantity: 10);
        item.Reserve(3, UtcNow);

        var result = item.Commit(3, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);
    }

    [Fact]
    public void Commit_ExceedingReserved_Fails()
    {
        var item = CreateItem(onHandQuantity: 10);
        item.Reserve(2, UtcNow);

        var result = item.Commit(3, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.CommitExceedsReserved, result.Error);
        Assert.Equal(10, item.OnHandQuantity);
        Assert.Equal(2, item.ReservedQuantity);
    }

    [Fact]
    public void Commit_WithInvalidQuantity_Fails()
    {
        var item = CreateItem(onHandQuantity: 10);
        item.Reserve(2, UtcNow);

        var result = item.Commit(0, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidQuantity, result.Error);
    }

    [Fact]
    public void FailedReserve_DoesNotIncrementVersion()
    {
        var item = CreateItem(onHandQuantity: 1);
        item.Reserve(1, UtcNow);

        item.Reserve(1, UtcNow);

        Assert.Equal(2, item.Version);
    }

    // ---- Hydrate ----

    [Fact]
    public void Hydrate_ReconstitutesItem()
    {
        var item = CreateItem(onHandQuantity: 10);
        item.Reserve(3, UtcNow);
        item.AddStock(5, UtcNow);

        var hydrated = InventoryItem.Hydrate(
            item.Sku,
            item.OnHandQuantity,
            item.ReservedQuantity,
            item.Version,
            item.CreatedAtUtc,
            item.UpdatedAtUtc);

        Assert.Equal(item.Sku, hydrated.Sku);
        Assert.Equal(item.OnHandQuantity, hydrated.OnHandQuantity);
        Assert.Equal(item.ReservedQuantity, hydrated.ReservedQuantity);
        Assert.Equal(item.AvailableQuantity, hydrated.AvailableQuantity);
        Assert.Equal(item.Version, hydrated.Version);
        Assert.Equal(item.CreatedAtUtc, hydrated.CreatedAtUtc);
        Assert.Equal(item.UpdatedAtUtc, hydrated.UpdatedAtUtc);
        Assert.Empty(hydrated.GetDomainEvents());
    }

    [Fact]
    public void Hydrate_WithNegativeOnHand_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryItem.Hydrate(new InventorySku("SKU-1"), -1, 0, InventoryItem.InitialVersion, UtcNow, UtcNow));
    }

    [Fact]
    public void Hydrate_WithNegativeReserved_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryItem.Hydrate(new InventorySku("SKU-1"), 10, -1, InventoryItem.InitialVersion, UtcNow, UtcNow));
    }

    [Fact]
    public void Hydrate_WithReservedExceedingOnHand_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryItem.Hydrate(new InventorySku("SKU-1"), 5, 6, InventoryItem.InitialVersion, UtcNow, UtcNow));
    }

    [Fact]
    public void Hydrate_WithInvalidVersion_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryItem.Hydrate(new InventorySku("SKU-1"), 10, 0, 0, UtcNow, UtcNow));
    }

    [Fact]
    public void Hydrate_WithDefaultTimestamps_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryItem.Hydrate(new InventorySku("SKU-1"), 10, 0, InventoryItem.InitialVersion, default, UtcNow));

        Assert.Throws<ArgumentException>(() =>
            InventoryItem.Hydrate(new InventorySku("SKU-1"), 10, 0, InventoryItem.InitialVersion, UtcNow, default));
    }

    [Fact]
    public void Hydrate_WithUpdatePrecedingCreation_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryItem.Hydrate(
                new InventorySku("SKU-1"),
                10,
                0,
                InventoryItem.InitialVersion,
                UtcNow,
                UtcNow.AddMinutes(-1)));
    }
}
