using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.Errors;
using FluxCommerce.Modules.Inventory.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.Results;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class InventoryReservationTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ExpiresAt = new(2026, 2, 10, 8, 45, 0, TimeSpan.Zero);

    private static InventoryItem CreateItem(int onHandQuantity = 10) =>
        InventoryItem.Create(new InventorySku("SKU-1"), onHandQuantity, UtcNow);

    private static DomainResult<InventoryReservation> Reserve(InventoryItem item, int quantity = 3) =>
        InventoryReservation.Reserve(
            item,
            InventoryReservationId.Create(),
            OrderId.Create(),
            quantity,
            ExpiresAt,
            UtcNow);

    // ---- Reservation creation ----

    [Fact]
    public void Reserve_WithSufficientStock_CreatesPendingReservation()
    {
        var item = CreateItem(onHandQuantity: 10);

        var result = Reserve(item, quantity: 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(InventoryReservationStatus.Pending, result.Value.Status);
        Assert.Equal(3, result.Value.Quantity);
        Assert.Equal(item.Sku, result.Value.Sku);
        Assert.Equal(ExpiresAt, result.Value.ExpiresAt);
        Assert.Equal(UtcNow, result.Value.CreatedAtUtc);
        Assert.Equal(InventoryReservation.InitialVersion, result.Value.Version);
    }

    [Fact]
    public void Reserve_MakesStockUnavailableOnItem()
    {
        var item = CreateItem(onHandQuantity: 10);

        var result = Reserve(item, quantity: 3);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, item.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);
        Assert.Equal(2, item.Version);
    }

    [Fact]
    public void Reserve_WithInsufficientStock_FailsAndLeavesItemUnchanged()
    {
        var item = CreateItem(onHandQuantity: 3);

        var result = Reserve(item, quantity: 4);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientStock, result.Error);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(3, item.AvailableQuantity);
        Assert.Equal(InventoryItem.InitialVersion, item.Version);
    }

    [Fact]
    public void Reserve_WithInvalidQuantity_FailsAndLeavesItemUnchanged()
    {
        var item = CreateItem(onHandQuantity: 10);

        var result = Reserve(item, quantity: 0);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidQuantity, result.Error);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Reserve_WithPastExpiration_FailsAndLeavesItemUnchanged()
    {
        var item = CreateItem(onHandQuantity: 10);

        var result = InventoryReservation.Reserve(
            item,
            InventoryReservationId.Create(),
            OrderId.Create(),
            1,
            UtcNow,
            UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidExpiration, result.Error);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Reserve_CompetingReservations_OnlyFirstSucceeds()
    {
        var item = CreateItem(onHandQuantity: 1);

        Assert.True(Reserve(item, quantity: 1).IsSuccess);
        var second = Reserve(item, quantity: 1);

        Assert.True(second.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientStock, second.Error);
    }

    [Fact]
    public void Reserve_WithNullItem_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InventoryReservation.Reserve(null!, InventoryReservationId.Create(), OrderId.Create(), 1, ExpiresAt, UtcNow));
    }

    [Fact]
    public void Reserve_RaisesInventoryReservationCreated()
    {
        var item = CreateItem(onHandQuantity: 10);

        var result = Reserve(item, quantity: 3);

        Assert.True(result.IsSuccess);
        var domainEvent = Assert.Single(result.Value.GetDomainEvents().OfType<InventoryReservationCreated>());
        Assert.Equal(result.Value.Id, domainEvent.ReservationId);
        Assert.Equal(result.Value.OrderId, domainEvent.OrderId);
        Assert.Equal(result.Value.Sku, domainEvent.Sku);
        Assert.Equal(3, domainEvent.Quantity);
        Assert.Equal(ExpiresAt, domainEvent.ExpiresAt);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    // ---- Commit ----

    [Fact]
    public void Commit_TransitionsToCommittedAndConsumesStock()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;

        var result = reservation.Commit(item, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
        Assert.Equal(7, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);
    }

    [Fact]
    public void Commit_RaisesInventoryReservationCommitted()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;

        reservation.Commit(item, UtcNow);

        var domainEvent = Assert.Single(reservation.GetDomainEvents().OfType<InventoryReservationCommitted>());
        Assert.Equal(reservation.Id, domainEvent.ReservationId);
        Assert.Equal(reservation.OrderId, domainEvent.OrderId);
        Assert.Equal(reservation.Sku, domainEvent.Sku);
        Assert.Equal(3, domainEvent.Quantity);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    [Fact]
    public void Commit_WhenAlreadyCommitted_FailsAndDoesNotConsumeTwice()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Commit(item, UtcNow);

        var second = reservation.Commit(item, UtcNow);

        Assert.True(second.IsFailure);
        Assert.Equal(InventoryErrors.InvalidStateTransition, second.Error);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
        Assert.Equal(7, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Commit_WhenAlreadyReleased_Fails()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Release(item, UtcNow);

        var result = reservation.Commit(item, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidStateTransition, result.Error);
    }

    // ---- Release ----

    [Fact]
    public void Release_TransitionsToReleasedAndReleasesStock()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;

        var result = reservation.Release(item, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void Release_RaisesInventoryReservationReleased()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;

        reservation.Release(item, UtcNow);

        var domainEvent = Assert.Single(reservation.GetDomainEvents().OfType<InventoryReservationReleased>());
        Assert.Equal(reservation.Id, domainEvent.ReservationId);
        Assert.Equal(reservation.OrderId, domainEvent.OrderId);
        Assert.Equal(3, domainEvent.Quantity);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    [Fact]
    public void Release_WhenAlreadyReleased_FailsAndDoesNotReleaseTwice()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Release(item, UtcNow);

        var second = reservation.Release(item, UtcNow);

        Assert.True(second.IsFailure);
        Assert.Equal(InventoryErrors.InvalidStateTransition, second.Error);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Release_WhenAlreadyCommitted_Fails()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Commit(item, UtcNow);

        var result = reservation.Release(item, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidStateTransition, result.Error);
    }

    // ---- Expire ----

    [Fact]
    public void Expire_TransitionsToExpiredAndReleasesStock()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;

        var result = reservation.Expire(item, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);
    }

    [Fact]
    public void Expire_RaisesInventoryReservationExpired()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;

        reservation.Expire(item, UtcNow);

        var domainEvent = Assert.Single(reservation.GetDomainEvents().OfType<InventoryReservationExpired>());
        Assert.Equal(reservation.Id, domainEvent.ReservationId);
        Assert.Equal(reservation.OrderId, domainEvent.OrderId);
        Assert.Equal(3, domainEvent.Quantity);
        Assert.Equal(UtcNow, domainEvent.OccurredAt);
    }

    [Fact]
    public void Expire_WhenAlreadyExpired_FailsAndDoesNotReleaseTwice()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Expire(item, UtcNow);

        var second = reservation.Expire(item, UtcNow);

        Assert.True(second.IsFailure);
        Assert.Equal(InventoryErrors.InvalidStateTransition, second.Error);
        Assert.Equal(InventoryReservationStatus.Expired, reservation.Status);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Expire_WhenAlreadyReleased_FailsAndDoesNotRaiseExpired()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Release(item, UtcNow);
        reservation.ClearDomainEvents();

        var result = reservation.Expire(item, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InvalidStateTransition, result.Error);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Empty(reservation.GetDomainEvents());
        Assert.Equal(0, item.ReservedQuantity);
    }

    // ---- Terminal state protection ----

    [Fact]
    public void TerminalReservations_RejectAllLifecycleOperations()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Commit(item, UtcNow);

        Assert.True(reservation.Commit(item, UtcNow).IsFailure);
        Assert.True(reservation.Release(item, UtcNow).IsFailure);
        Assert.True(reservation.Expire(item, UtcNow).IsFailure);
        Assert.Equal(InventoryReservationStatus.Committed, reservation.Status);
    }

    [Fact]
    public void FailedTransition_DoesNotIncrementVersion()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Commit(item, UtcNow);

        var before = reservation.Version;
        reservation.Commit(item, UtcNow);

        Assert.Equal(before, reservation.Version);
    }

    [Fact]
    public void FailedTransition_DoesNotRaiseEvent()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Commit(item, UtcNow);
        reservation.ClearDomainEvents();

        reservation.Release(item, UtcNow);

        Assert.Empty(reservation.GetDomainEvents());
    }

    // ---- Hydrate ----

    [Fact]
    public void Hydrate_ReconstitutesReservation()
    {
        var item = CreateItem(onHandQuantity: 10);
        var reservation = Reserve(item, quantity: 3).Value;
        reservation.Commit(item, UtcNow);

        var hydrated = InventoryReservation.Hydrate(
            reservation.Id,
            reservation.OrderId,
            reservation.Sku,
            reservation.Quantity,
            reservation.Status,
            reservation.ExpiresAt,
            reservation.Version,
            reservation.CreatedAtUtc,
            reservation.UpdatedAtUtc);

        Assert.Equal(reservation.Id, hydrated.Id);
        Assert.Equal(reservation.OrderId, hydrated.OrderId);
        Assert.Equal(reservation.Sku, hydrated.Sku);
        Assert.Equal(reservation.Quantity, hydrated.Quantity);
        Assert.Equal(reservation.Status, hydrated.Status);
        Assert.Equal(reservation.ExpiresAt, hydrated.ExpiresAt);
        Assert.Equal(reservation.Version, hydrated.Version);
        Assert.Equal(reservation.CreatedAtUtc, hydrated.CreatedAtUtc);
        Assert.Equal(reservation.UpdatedAtUtc, hydrated.UpdatedAtUtc);
        Assert.Empty(hydrated.GetDomainEvents());
    }

    [Fact]
    public void Hydrate_WithInvalidQuantity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryReservation.Hydrate(
                InventoryReservationId.Create(),
                OrderId.Create(),
                new InventorySku("SKU-1"),
                0,
                InventoryReservationStatus.Pending,
                ExpiresAt,
                InventoryReservation.InitialVersion,
                UtcNow,
                UtcNow));
    }

    [Fact]
    public void Hydrate_WithDefaultExpiration_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryReservation.Hydrate(
                InventoryReservationId.Create(),
                OrderId.Create(),
                new InventorySku("SKU-1"),
                1,
                InventoryReservationStatus.Pending,
                default,
                InventoryReservation.InitialVersion,
                UtcNow,
                UtcNow));
    }

    [Fact]
    public void Hydrate_WithInvalidVersion_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryReservation.Hydrate(
                InventoryReservationId.Create(),
                OrderId.Create(),
                new InventorySku("SKU-1"),
                1,
                InventoryReservationStatus.Pending,
                ExpiresAt,
                0,
                UtcNow,
                UtcNow));
    }
}
