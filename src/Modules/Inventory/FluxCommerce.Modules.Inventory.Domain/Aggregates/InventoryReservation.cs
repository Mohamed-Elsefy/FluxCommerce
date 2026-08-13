using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.Errors;
using FluxCommerce.Modules.Inventory.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.Results;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Domain.Aggregates;

public sealed class InventoryReservation : AggregateRoot<InventoryReservationId>
{
    public const long InitialVersion = 1;

    public OrderId OrderId { get; }

    public InventorySku Sku { get; }

    public int Quantity { get; }

    public InventoryReservationStatus Status { get; private set; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc => _updatedAtUtc;

    public long Version => _version;

    private long _version;

    private DateTimeOffset _updatedAtUtc;

    private InventoryReservation(
        InventoryReservationId id,
        OrderId orderId,
        InventorySku sku,
        int quantity,
        InventoryReservationStatus status,
        DateTimeOffset expiresAt,
        long version,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        OrderId = orderId;
        Sku = sku;
        Quantity = quantity;
        Status = status;
        ExpiresAt = expiresAt;
        _version = version;
        CreatedAtUtc = createdAtUtc;
        _updatedAtUtc = createdAtUtc;
    }

    #pragma warning disable CS8618
    private InventoryReservation() { }
    #pragma warning restore CS8618

    public static DomainResult<InventoryReservation> Reserve(
        InventoryItem item,
        InventoryReservationId reservationId,
        OrderId orderId,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        if (reservationId is null)
            throw new ArgumentNullException(nameof(reservationId));

        if (orderId is null)
            throw new ArgumentNullException(nameof(orderId));

        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (quantity < 1)
            return DomainResult.Failure<InventoryReservation>(InventoryErrors.InvalidQuantity);

        if (expiresAt <= utcNow)
            return DomainResult.Failure<InventoryReservation>(InventoryErrors.InvalidExpiration);

        var stockResult = item.Reserve(quantity, utcNow);

        if (stockResult.IsFailure)
            return DomainResult.Failure<InventoryReservation>(stockResult.Error);

        var reservation = new InventoryReservation(
            reservationId,
            orderId,
            item.Sku,
            quantity,
            InventoryReservationStatus.Pending,
            expiresAt,
            InitialVersion,
            utcNow);

        reservation.AddDomainEvent(new InventoryReservationCreated(
            reservationId,
            orderId,
            item.Sku,
            quantity,
            expiresAt,
            utcNow));

        return DomainResult.Success(reservation);
    }

    /// <summary>
    /// Constructs a Pending reservation for stock that was already atomically reserved by the caller.
    /// Unlike <see cref="Reserve"/>, the inventory item is not mutated here: the caller must have
    /// applied the reserved-quantity increment in the same atomic operation that verifies
    /// availability. The <see cref="InventoryReservationCreated"/> event is recorded so the creation
    /// is still observable.
    /// </summary>
    public static DomainResult<InventoryReservation> CreateReserved(
        InventoryReservationId reservationId,
        OrderId orderId,
        InventorySku sku,
        int quantity,
        DateTimeOffset expiresAt,
        DateTimeOffset utcNow)
    {
        if (reservationId is null)
            throw new ArgumentNullException(nameof(reservationId));

        if (orderId is null)
            throw new ArgumentNullException(nameof(orderId));

        if (sku is null)
            throw new ArgumentNullException(nameof(sku));

        if (quantity < 1)
            return DomainResult.Failure<InventoryReservation>(InventoryErrors.InvalidQuantity);

        if (expiresAt <= utcNow)
            return DomainResult.Failure<InventoryReservation>(InventoryErrors.InvalidExpiration);

        var reservation = new InventoryReservation(
            reservationId,
            orderId,
            sku,
            quantity,
            InventoryReservationStatus.Pending,
            expiresAt,
            InitialVersion,
            utcNow);

        reservation.AddDomainEvent(new InventoryReservationCreated(
            reservationId,
            orderId,
            sku,
            quantity,
            expiresAt,
            utcNow));

        return DomainResult.Success(reservation);
    }

    public static InventoryReservation Hydrate(
        InventoryReservationId id,
        OrderId orderId,
        InventorySku sku,
        int quantity,
        InventoryReservationStatus status,
        DateTimeOffset expiresAt,
        long version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (id is null)
            throw new ArgumentNullException(nameof(id));

        if (orderId is null)
            throw new ArgumentNullException(nameof(orderId));

        if (sku is null)
            throw new ArgumentNullException(nameof(sku));

        if (quantity < 1)
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "A reservation quantity must be greater than zero.");

        if (expiresAt == default)
            throw new ArgumentException("A valid expiration timestamp is required.", nameof(expiresAt));

        if (version < InitialVersion)
            throw new ArgumentException(
                $"A reservation version must be at least {InitialVersion}.",
                nameof(version));

        if (createdAtUtc == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(createdAtUtc));

        if (updatedAtUtc == default)
            throw new ArgumentException("A valid update timestamp is required.", nameof(updatedAtUtc));

        if (updatedAtUtc < createdAtUtc)
            throw new ArgumentException(
                "The update timestamp must not precede the creation timestamp.",
                nameof(updatedAtUtc));

        var reservation = new InventoryReservation(
            id,
            orderId,
            sku,
            quantity,
            status,
            expiresAt,
            version,
            createdAtUtc);

        reservation._updatedAtUtc = updatedAtUtc;

        return reservation;
    }

    public DomainResult Commit(InventoryItem item, DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (Status != InventoryReservationStatus.Pending)
            return DomainResult.Failure(InventoryErrors.InvalidStateTransition);

        var stockResult = item.Commit(Quantity, utcNow);

        if (stockResult.IsFailure)
            return stockResult;

        Status = InventoryReservationStatus.Committed;

        Touch(utcNow);

        AddDomainEvent(new InventoryReservationCommitted(Id, OrderId, Sku, Quantity, utcNow));

        return DomainResult.Success();
    }

    public DomainResult Release(InventoryItem item, DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (Status != InventoryReservationStatus.Pending)
            return DomainResult.Failure(InventoryErrors.InvalidStateTransition);

        var stockResult = item.Release(Quantity, utcNow);

        if (stockResult.IsFailure)
            return stockResult;

        Status = InventoryReservationStatus.Released;

        Touch(utcNow);

        AddDomainEvent(new InventoryReservationReleased(Id, OrderId, Sku, Quantity, utcNow));

        return DomainResult.Success();
    }

    public DomainResult Expire(InventoryItem item, DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (Status != InventoryReservationStatus.Pending)
            return DomainResult.Failure(InventoryErrors.InvalidStateTransition);

        var stockResult = item.Release(Quantity, utcNow);

        if (stockResult.IsFailure)
            return stockResult;

        Status = InventoryReservationStatus.Expired;

        Touch(utcNow);

        AddDomainEvent(new InventoryReservationExpired(Id, OrderId, Sku, Quantity, utcNow));

        return DomainResult.Success();
    }

    private void Touch(DateTimeOffset utcNow)
    {
        _version++;
        _updatedAtUtc = utcNow;
    }
}
