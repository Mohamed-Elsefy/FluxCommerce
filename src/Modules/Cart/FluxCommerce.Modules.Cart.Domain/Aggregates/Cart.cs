using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.Errors;
using FluxCommerce.Modules.Cart.Domain.Results;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Domain.Aggregates;

public sealed class Cart : AggregateRoot<CartId>
{
    public const long InitialVersion = 1;

    public CartOwner Owner { get; }

    public Guid? UserId => Owner.UserId;

    public bool IsGuest => Owner.IsGuest;

    public bool IsAuthenticated => Owner.IsAuthenticated;

    public long Version => _version;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc => _updatedAtUtc;

    private long _version;

    private DateTimeOffset _updatedAtUtc;

    private readonly List<CartItem> _items = [];

    public IReadOnlyCollection<CartItem> Items => _items;

    private Cart(CartId id, CartOwner owner, long version, DateTimeOffset createdAtUtc)
        : base(id)
    {
        if (createdAtUtc == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(createdAtUtc));

        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _version = version;
        CreatedAtUtc = createdAtUtc;
        _updatedAtUtc = createdAtUtc;
    }

    #pragma warning disable CS8618
    private Cart() { }
    #pragma warning restore CS8618

    public static Cart CreateGuest(CartId id, DateTimeOffset utcNow)
    {
        if (id is null)
            throw new ArgumentNullException(nameof(id));

        return new Cart(id, CartOwner.Guest(), InitialVersion, utcNow);
    }

    public static Cart CreateAuthenticated(CartId id, Guid userId, DateTimeOffset utcNow)
    {
        if (id is null)
            throw new ArgumentNullException(nameof(id));

        return new Cart(id, CartOwner.Authenticated(userId), InitialVersion, utcNow);
    }

    public DomainResult AddItem(
        ProductId productId,
        VariantId variantId,
        Sku sku,
        string productName,
        Money unitPrice,
        int quantity,
        DateTimeOffset utcNow)
    {
        if (quantity < 1)
            return DomainResult.Failure(CartErrors.InvalidQuantity);

        var existing = _items.FirstOrDefault(item => item.VariantId == variantId);

        if (existing is not null)
        {
            var newQuantity = (long)existing.Quantity + quantity;

            if (newQuantity > int.MaxValue)
                return DomainResult.Failure(CartErrors.InvalidQuantity);

            existing.ChangeQuantity((int)newQuantity);

            Touch(utcNow);

            return DomainResult.Success();
        }

        _items.Add(new CartItem(productId, variantId, sku, productName, unitPrice, quantity));

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult UpdateQuantity(VariantId variantId, int quantity, DateTimeOffset utcNow)
    {
        if (variantId is null)
            throw new ArgumentNullException(nameof(variantId));

        if (quantity < 1)
            return DomainResult.Failure(CartErrors.InvalidQuantity);

        var item = _items.FirstOrDefault(item => item.VariantId == variantId);

        if (item is null)
            return DomainResult.Failure(CartErrors.CartItemNotFound);

        if (item.Quantity == quantity)
            return DomainResult.Success();

        item.ChangeQuantity(quantity);

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult RemoveItem(VariantId variantId, DateTimeOffset utcNow)
    {
        if (variantId is null)
            throw new ArgumentNullException(nameof(variantId));

        var item = _items.FirstOrDefault(item => item.VariantId == variantId);

        if (item is null)
            return DomainResult.Failure(CartErrors.CartItemNotFound);

        _items.Remove(item);

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult Clear(DateTimeOffset utcNow)
    {
        if (_items.Count == 0)
            return DomainResult.Success();

        _items.Clear();

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult Merge(Cart source, DateTimeOffset utcNow)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        if (source.Id == Id)
            throw new ArgumentException("A cart cannot be merged with itself.", nameof(source));

        if (source.IsAuthenticated)
            return DomainResult.Failure(CartErrors.InvalidMergeSource);

        if (source.Items.Count == 0)
            return DomainResult.Success();

        foreach (var sourceItem in source.Items)
        {
            var targetItem = _items.FirstOrDefault(item => item.VariantId == sourceItem.VariantId);

            if (targetItem is null)
                continue;

            if ((long)targetItem.Quantity + sourceItem.Quantity > int.MaxValue)
                return DomainResult.Failure(CartErrors.InvalidQuantity);
        }

        foreach (var sourceItem in source.Items)
        {
            var targetItem = _items.FirstOrDefault(item => item.VariantId == sourceItem.VariantId);

            if (targetItem is not null)
            {
                targetItem.ChangeQuantity(targetItem.Quantity + sourceItem.Quantity);
            }
            else
            {
                _items.Add(new CartItem(
                    sourceItem.ProductId,
                    sourceItem.VariantId,
                    sourceItem.Sku,
                    sourceItem.ProductName,
                    sourceItem.UnitPrice,
                    sourceItem.Quantity));
            }
        }

        Touch(utcNow);

        source.Clear(utcNow);

        return DomainResult.Success();
    }

    public bool ContainsVariant(VariantId variantId)
    {
        if (variantId is null)
            throw new ArgumentNullException(nameof(variantId));

        return _items.Any(item => item.VariantId == variantId);
    }

    private void Touch(DateTimeOffset utcNow)
    {
        _version++;
        _updatedAtUtc = utcNow;
    }
}
