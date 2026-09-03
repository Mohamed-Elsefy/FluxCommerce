using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Inventory.Domain.Errors;
using FluxCommerce.Modules.Inventory.Domain.Events;
using FluxCommerce.Modules.Inventory.Domain.Results;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;

namespace FluxCommerce.Modules.Inventory.Domain.Aggregates;

public sealed class InventoryItem : AggregateRoot<InventorySku>
{
    public const long InitialVersion = 1;

    public InventorySku Sku => Id;

    public int OnHandQuantity { get; private set; }

    public int ReservedQuantity { get; private set; }

    public int AvailableQuantity => OnHandQuantity - ReservedQuantity;

    public long Version => _version;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc => _updatedAtUtc;

    private long _version;

    private DateTimeOffset _updatedAtUtc;

    private InventoryItem(
        InventorySku sku,
        int onHandQuantity,
        int reservedQuantity,
        long version,
        DateTimeOffset createdAtUtc)
        : base(sku)
    {
        OnHandQuantity = onHandQuantity;
        ReservedQuantity = reservedQuantity;
        _version = version;
        CreatedAtUtc = createdAtUtc;
        _updatedAtUtc = createdAtUtc;
    }

    #pragma warning disable CS8618
    private InventoryItem() { }
    #pragma warning restore CS8618

    public static InventoryItem Create(InventorySku sku, int onHandQuantity, DateTimeOffset utcNow)
    {
        if (sku is null)
            throw new ArgumentNullException(nameof(sku));

        if (onHandQuantity < 0)
            throw new ArgumentOutOfRangeException(
                nameof(onHandQuantity),
                "The on-hand quantity must not be negative.");

        if (utcNow == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(utcNow));

        var item = new InventoryItem(sku, onHandQuantity, 0, InitialVersion, utcNow);

        item.AddDomainEvent(new InventoryItemCreated(sku, onHandQuantity, utcNow));

        return item;
    }

    public static InventoryItem Hydrate(
        InventorySku sku,
        int onHandQuantity,
        int reservedQuantity,
        long version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (sku is null)
            throw new ArgumentNullException(nameof(sku));

        if (onHandQuantity < 0)
            throw new ArgumentOutOfRangeException(
                nameof(onHandQuantity),
                "The on-hand quantity must not be negative.");

        if (reservedQuantity < 0)
            throw new ArgumentOutOfRangeException(
                nameof(reservedQuantity),
                "The reserved quantity must not be negative.");

        if (reservedQuantity > onHandQuantity)
            throw new ArgumentException(
                "The reserved quantity must not exceed the on-hand quantity.",
                nameof(reservedQuantity));

        if (version < InitialVersion)
            throw new ArgumentException(
                $"An inventory item version must be at least {InitialVersion}.",
                nameof(version));

        if (createdAtUtc == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(createdAtUtc));

        if (updatedAtUtc == default)
            throw new ArgumentException("A valid update timestamp is required.", nameof(updatedAtUtc));

        if (updatedAtUtc < createdAtUtc)
            throw new ArgumentException(
                "The update timestamp must not precede the creation timestamp.",
                nameof(updatedAtUtc));

        var item = new InventoryItem(sku, onHandQuantity, reservedQuantity, version, createdAtUtc);

        item._updatedAtUtc = updatedAtUtc;

        return item;
    }

    public DomainResult AddStock(int quantity, DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (quantity < 1)
            return DomainResult.Failure(InventoryErrors.InvalidQuantity);

        OnHandQuantity += quantity;

        Touch(utcNow);

        AddDomainEvent(new InventoryStockAdded(Sku, quantity, OnHandQuantity, utcNow));

        return DomainResult.Success();
    }

    public DomainResult Reserve(int quantity, DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (quantity < 1)
            return DomainResult.Failure(InventoryErrors.InvalidQuantity);

        if (AvailableQuantity < quantity)
            return DomainResult.Failure(InventoryErrors.InsufficientStock);

        ReservedQuantity += quantity;

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult Release(int quantity, DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (quantity < 1)
            return DomainResult.Failure(InventoryErrors.InvalidQuantity);

        if (ReservedQuantity < quantity)
            return DomainResult.Failure(InventoryErrors.ReleaseExceedsReserved);

        ReservedQuantity -= quantity;

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult Commit(int quantity, DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        if (quantity < 1)
            return DomainResult.Failure(InventoryErrors.InvalidQuantity);

        if (ReservedQuantity < quantity)
            return DomainResult.Failure(InventoryErrors.CommitExceedsReserved);

        OnHandQuantity -= quantity;
        ReservedQuantity -= quantity;

        Touch(utcNow);

        return DomainResult.Success();
    }

    private void Touch(DateTimeOffset utcNow)
    {
        _version++;
        _updatedAtUtc = utcNow;
    }
}
