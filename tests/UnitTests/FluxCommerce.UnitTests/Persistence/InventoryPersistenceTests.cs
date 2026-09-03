using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

public class InventoryPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpiresAt = new(2026, 2, 10, 8, 45, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public InventoryPersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    // ---- InventoryItem ----

    [Fact]
    public async Task InventoryItem_RoundTrips_WithAllProperties()
    {
        await using var context = CreateContext();

        var item = InventoryItem.Create(new InventorySku("SKU-1"), 10, UtcNow);

        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();

        var loaded = await context.InventoryItems.SingleAsync(i => i.Id == new InventorySku("SKU-1"));

        Assert.Equal(new InventorySku("SKU-1"), loaded.Id);
        Assert.Equal(new InventorySku("SKU-1"), loaded.Sku);
        Assert.Equal(10, loaded.OnHandQuantity);
        Assert.Equal(0, loaded.ReservedQuantity);
        Assert.Equal(10, loaded.AvailableQuantity);
        Assert.Equal(InventoryItem.InitialVersion, loaded.Version);
        Assert.Equal(UtcNow, loaded.CreatedAtUtc);
        Assert.Equal(UtcNow, loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task InventoryItem_StockAdjustments_RoundTrip()
    {
        await using var context = CreateContext();

        var item = InventoryItem.Create(new InventorySku("SKU-ADJ"), 10, UtcNow);
        item.AddStock(5, UtcNow);

        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();

        var loaded = await context.InventoryItems.SingleAsync(i => i.Id == new InventorySku("SKU-ADJ"));

        Assert.Equal(15, loaded.OnHandQuantity);
        Assert.Equal(0, loaded.ReservedQuantity);
        Assert.Equal(15, loaded.AvailableQuantity);
        Assert.Equal(2, loaded.Version);
    }

    [Fact]
    public async Task InventoryItem_ReservationState_RoundTrips()
    {
        await using var context = CreateContext();

        var item = InventoryItem.Create(new InventorySku("SKU-RES"), 10, UtcNow);
        item.Reserve(3, UtcNow);

        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();

        var loaded = await context.InventoryItems.SingleAsync(i => i.Id == new InventorySku("SKU-RES"));

        Assert.Equal(10, loaded.OnHandQuantity);
        Assert.Equal(3, loaded.ReservedQuantity);
        Assert.Equal(7, loaded.AvailableQuantity);
    }

    [Fact]
    public async Task InventoryItem_RejectsDuplicateSku()
    {
        await using var seedContext = CreateContext();

        seedContext.InventoryItems.Add(InventoryItem.Create(new InventorySku("SKU-DUP"), 5, UtcNow));
        await seedContext.SaveChangesAsync();

        await using var context = CreateContext();

        context.InventoryItems.Add(InventoryItem.Create(new InventorySku("SKU-DUP"), 5, UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task InventoryItem_NonNegativeQuantityConstraint_IsEnforcedByDatabase()
    {
        await using var context = CreateContext();

        await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "INSERT INTO InventoryItems (Sku, OnHandQuantity, ReservedQuantity, Version, CreatedAtUtc, UpdatedAtUtc) " +
                "VALUES ('SKU-BAD', -1, 0, 1, '2026-02-10T08:15:00+00:00', '2026-02-10T08:15:00+00:00')"));
    }

    [Fact]
    public async Task InventoryItem_AvailableQuantity_IsComputedAndNotPersisted()
    {
        await using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(InventoryItem));

        Assert.NotNull(entityType);
        Assert.DoesNotContain(entityType!.GetProperties(), property => property.Name == nameof(InventoryItem.AvailableQuantity));
    }

    [Fact]
    public void InventoryItem_QuantityCheckConstraints_AreDefined()
    {
        using var context = CreateContext();

        var entityType = GetDesignTimeEntityType<InventoryItem>(context);

        var checkConstraints = entityType.GetCheckConstraints().Select(c => c.Name).ToList();

        Assert.Contains("CK_InventoryItems_OnHandQuantity_NonNegative", checkConstraints);
        Assert.Contains("CK_InventoryItems_ReservedQuantity_NonNegative", checkConstraints);
        Assert.Contains("CK_InventoryItems_Reserved_NotExceeds_OnHand", checkConstraints);
    }

    // ---- Optimistic concurrency ----

    [Fact]
    public async Task InventoryItem_StaleConcurrentUpdate_ThrowsConcurrencyException()
    {
        var sku = new InventorySku("SKU-CONC");

        await using (var seedContext = CreateContext())
        {
            seedContext.InventoryItems.Add(InventoryItem.Create(sku, 10, UtcNow));
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = CreateContext();
        var first = await firstContext.InventoryItems.SingleAsync(i => i.Id == sku);

        await using var secondContext = CreateContext();
        var second = await secondContext.InventoryItems.SingleAsync(i => i.Id == sku);
        second.AddStock(1, UtcNow);
        await secondContext.SaveChangesAsync();

        first.AddStock(1, UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => firstContext.SaveChangesAsync());
    }

    [Fact]
    public async Task InventoryReservation_StaleConcurrentUpdate_ThrowsConcurrencyException()
    {
        var item = InventoryItem.Create(new InventorySku("SKU-CONC-R"), 10, UtcNow);
        var reservation = InventoryReservation.Reserve(
            item,
            InventoryReservationId.Create(),
            OrderId.Create(),
            3,
            ExpiresAt,
            UtcNow).Value;

        await using (var seedContext = CreateContext())
        {
            seedContext.InventoryItems.Add(item);
            seedContext.InventoryReservations.Add(reservation);
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext = CreateContext();
        var first = await firstContext.InventoryReservations.SingleAsync(r => r.Id == reservation.Id);
        var firstItem = await firstContext.InventoryItems.SingleAsync(i => i.Id == item.Sku);

        await using var secondContext = CreateContext();
        var second = await secondContext.InventoryReservations.SingleAsync(r => r.Id == reservation.Id);
        var secondItem = await secondContext.InventoryItems.SingleAsync(i => i.Id == item.Sku);
        Assert.True(second.Commit(secondItem, UtcNow).IsSuccess);
        await secondContext.SaveChangesAsync();

        Assert.True(first.Commit(firstItem, UtcNow).IsSuccess);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => firstContext.SaveChangesAsync());
    }

    // ---- InventoryReservation ----

    [Fact]
    public async Task InventoryReservation_RoundTrips_WithAllProperties()
    {
        await using var context = CreateContext();

        var item = InventoryItem.Create(new InventorySku("SKU-RT"), 10, UtcNow);
        var reservationId = InventoryReservationId.Create();
        var orderId = OrderId.Create();

        var reservation = InventoryReservation.Reserve(
            item,
            reservationId,
            orderId,
            3,
            ExpiresAt,
            UtcNow).Value;

        context.InventoryItems.Add(item);
        context.InventoryReservations.Add(reservation);
        await context.SaveChangesAsync();

        var loaded = await context.InventoryReservations.SingleAsync(r => r.Id == reservationId);

        Assert.Equal(reservationId, loaded.Id);
        Assert.Equal(orderId, loaded.OrderId);
        Assert.Equal(new InventorySku("SKU-RT"), loaded.Sku);
        Assert.Equal(3, loaded.Quantity);
        Assert.Equal(InventoryReservationStatus.Pending, loaded.Status);
        Assert.Equal(ExpiresAt, loaded.ExpiresAt);
        Assert.Equal(InventoryReservation.InitialVersion, loaded.Version);
        Assert.Equal(UtcNow, loaded.CreatedAtUtc);
        Assert.Equal(UtcNow, loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task InventoryReservation_LifecycleStates_RoundTrip()
    {
        var item = InventoryItem.Create(new InventorySku("SKU-LC"), 10, UtcNow);
        var reservation = InventoryReservation.Reserve(
            item,
            InventoryReservationId.Create(),
            OrderId.Create(),
            3,
            ExpiresAt,
            UtcNow).Value;

        await using (var seedContext = CreateContext())
        {
            seedContext.InventoryItems.Add(item);
            seedContext.InventoryReservations.Add(reservation);
            await seedContext.SaveChangesAsync();
        }

        await using (var mutateContext = CreateContext())
        {
            var trackedItem = await mutateContext.InventoryItems.SingleAsync(i => i.Id == new InventorySku("SKU-LC"));
            var trackedReservation = await mutateContext.InventoryReservations.SingleAsync(r => r.Id == reservation.Id);

            trackedReservation.Commit(trackedItem, UtcNow);

            await mutateContext.SaveChangesAsync();
        }

        await using (var reloadContext = CreateContext())
        {
            var committed = await reloadContext.InventoryReservations.SingleAsync(r => r.Id == reservation.Id);
            var committedItem = await reloadContext.InventoryItems.SingleAsync(i => i.Id == new InventorySku("SKU-LC"));

            Assert.Equal(InventoryReservationStatus.Committed, committed.Status);
            Assert.Equal(7, committedItem.OnHandQuantity);
            Assert.Equal(0, committedItem.ReservedQuantity);
            Assert.Equal(7, committedItem.AvailableQuantity);
        }
    }

    [Fact]
    public async Task InventoryReservation_ExpirationState_RoundTrips()
    {
        var item = InventoryItem.Create(new InventorySku("SKU-EXP"), 10, UtcNow);
        var reservation = InventoryReservation.Reserve(
            item,
            InventoryReservationId.Create(),
            OrderId.Create(),
            3,
            ExpiresAt,
            UtcNow).Value;

        await using (var seedContext = CreateContext())
        {
            seedContext.InventoryItems.Add(item);
            seedContext.InventoryReservations.Add(reservation);
            await seedContext.SaveChangesAsync();
        }

        await using (var mutateContext = CreateContext())
        {
            var trackedItem = await mutateContext.InventoryItems.SingleAsync(i => i.Id == new InventorySku("SKU-EXP"));
            var trackedReservation = await mutateContext.InventoryReservations.SingleAsync(r => r.Id == reservation.Id);

            trackedReservation.Expire(trackedItem, UtcNow);

            await mutateContext.SaveChangesAsync();
        }

        await using var reloadContext = CreateContext();
        var expired = await reloadContext.InventoryReservations.SingleAsync(r => r.Id == reservation.Id);

        Assert.Equal(InventoryReservationStatus.Expired, expired.Status);
    }

    [Fact]
    public void InventoryReservation_HasNoForeignKeys()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(InventoryReservation));

        Assert.NotNull(entityType);
        Assert.Empty(entityType!.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
    }

    [Fact]
    public void InventoryItem_HasNoForeignKeys()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(InventoryItem));

        Assert.NotNull(entityType);
        Assert.Empty(entityType!.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
    }

    [Fact]
    public void InventoryReservation_LookupIndexes_AreDefined()
    {
        using var context = CreateContext();

        var entityType = GetDesignTimeEntityType<InventoryReservation>(context);

        var indexNames = entityType.GetIndexes().Select(index => index.GetDatabaseName()).ToList();

        Assert.True(
            indexNames.Contains("IX_InventoryReservations_Sku")
            && indexNames.Contains("IX_InventoryReservations_OrderId")
            && indexNames.Contains("IX_InventoryReservations_Status_ExpiresAt"),
            "Expected indexes [IX_InventoryReservations_Sku, IX_InventoryReservations_OrderId, IX_InventoryReservations_Status_ExpiresAt] " +
            $"but found [{string.Join(", ", indexNames)}]");
    }

    [Fact]
    public void InventoryReservation_QuantityCheckConstraint_IsDefined()
    {
        using var context = CreateContext();

        var entityType = GetDesignTimeEntityType<InventoryReservation>(context);

        Assert.Contains(
            "CK_InventoryReservations_Quantity_Positive",
            entityType.GetCheckConstraints().Select(c => c.Name));
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static IEntityType GetDesignTimeEntityType<TEntity>(InventoryDbContext context)
        where TEntity : class
    {
        return context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' was not found in the model.");
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
