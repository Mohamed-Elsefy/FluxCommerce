using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.Entities;

public sealed class OrderLineItem : Entity<OrderVariantId>
{
    public const int ProductNameMaxLength = 200;

    public OrderProductId ProductId { get; }

    public OrderVariantId VariantId => Id;

    public OrderSku Sku { get; }

    public string ProductName { get; }

    public OrderMoney UnitPrice { get; }

    public int Quantity { get; private set; }

    public OrderLineItem(
        OrderProductId productId,
        OrderVariantId variantId,
        OrderSku sku,
        string productName,
        OrderMoney unitPrice,
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
    private OrderLineItem() { }
    #pragma warning restore CS8618

    public OrderMoney LineTotal => UnitPrice.Multiply(Quantity);

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
