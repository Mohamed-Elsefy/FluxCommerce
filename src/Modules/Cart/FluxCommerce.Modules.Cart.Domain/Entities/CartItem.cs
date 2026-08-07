using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Cart.Domain.ValueObjects;

namespace FluxCommerce.Modules.Cart.Domain.Entities;

public sealed class CartItem : Entity<VariantId>
{
    public const int ProductNameMaxLength = 200;

    public ProductId ProductId { get; }

    public VariantId VariantId => Id;

    public Sku Sku { get; }

    public string ProductName { get; }

    public Money UnitPrice { get; }

    public int Quantity { get; private set; }

    internal CartItem(
        ProductId productId,
        VariantId variantId,
        Sku sku,
        string productName,
        Money unitPrice,
        int quantity)
        : base(variantId)
    {
        if (variantId is null)
            throw new ArgumentNullException(nameof(variantId));

        ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
        Sku = sku ?? throw new ArgumentNullException(nameof(sku));
        ProductName = ValidateProductName(productName, nameof(productName));
        UnitPrice = unitPrice ?? throw new ArgumentNullException(nameof(unitPrice));
        Quantity = ValidateQuantity(quantity, nameof(quantity));
    }

    #pragma warning disable CS8618
    private CartItem() { }
    #pragma warning restore CS8618

    internal void ChangeQuantity(int quantity)
    {
        if (quantity < 1)
            throw new ArgumentOutOfRangeException(nameof(quantity), "A quantity must be greater than zero.");

        Quantity = quantity;
    }

    private static string ValidateProductName(string? value, string paramName)
    {
        var productName = value?.Trim();

        if (string.IsNullOrEmpty(productName))
            throw new ArgumentException($"A {paramName} is required.", paramName);

        if (productName.Length > ProductNameMaxLength)
            throw new ArgumentException($"{paramName} must not exceed {ProductNameMaxLength} characters.", paramName);

        return productName;
    }

    private static int ValidateQuantity(int value, string paramName)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(paramName, "A quantity must be greater than zero.");

        return value;
    }
}
