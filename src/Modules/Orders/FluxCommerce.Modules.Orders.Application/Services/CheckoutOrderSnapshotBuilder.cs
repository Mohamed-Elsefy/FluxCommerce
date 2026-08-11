using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Services;

/// <summary>
/// Assembles the immutable Checkout Order snapshot for an authenticated customer using the current
/// (revalidated) Catalog data. The temporary unit price stored on the Cart snapshot is never treated
/// as authoritative; pricing and the commercial snapshot (product identity, SKU, product name, unit
/// price) always come from the revalidated Catalog state. Line totals, the subtotal and the Phase 4
/// totals (Discount = 0, Tax = 0, Shipping = 0, GrandTotal = Subtotal) are produced by the Order
/// aggregate itself so they stay internally consistent.
/// </summary>
public sealed class CheckoutOrderSnapshotBuilder : ICheckoutOrderSnapshotBuilder
{
    public Result<Order> Build(
        CartCheckoutSnapshot cart,
        IReadOnlyCollection<CatalogVariantCheckoutInfo> revalidation,
        CustomerId customerId,
        OrderNumber orderNumber,
        OrderAddress shippingAddress,
        OrderAddress billingAddress,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(revalidation);
        ArgumentNullException.ThrowIfNull(customerId);
        ArgumentNullException.ThrowIfNull(orderNumber);
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(billingAddress);

        if (utcNow == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(utcNow));

        if (cart.Items.Count == 0)
            return Result.Failure<Order>(CheckoutErrors.CartEmpty);

        var revalidatedById = revalidation.ToDictionary(info => info.VariantId);

        var lineItems = new OrderLineItem[cart.Items.Count];
        var currency = default(string);

        for (var i = 0; i < cart.Items.Count; i++)
        {
            var item = cart.Items[i];

            if (!revalidatedById.TryGetValue(item.VariantId, out var info))
                return Result.Failure<Order>(CheckoutErrors.VariantUnavailable);

            if (info.Status != CatalogVariantEligibilityStatus.Available
                || info.ProductId is null
                || info.Sku is null
                || info.ProductName is null
                || info.UnitPriceAmount is null
                || info.Currency is null)
            {
                return Result.Failure<Order>(CheckoutErrors.VariantUnavailable);
            }

            if (currency is null)
                currency = info.Currency;
            else if (!string.Equals(currency, info.Currency, StringComparison.Ordinal))
                return Result.Failure<Order>(CheckoutErrors.CurrencyMismatch);

            lineItems[i] = new OrderLineItem(
                new OrderProductId(info.ProductId.Value),
                new OrderVariantId(info.VariantId),
                new OrderSku(info.Sku),
                info.ProductName,
                new OrderMoney(info.UnitPriceAmount.Value, info.Currency),
                item.Quantity);
        }

        if (lineItems.Select(lineItem => lineItem.VariantId).Distinct().Count() != lineItems.Length)
            return Result.Failure<Order>(CheckoutErrors.DuplicateVariant);

        return Result.Success(
            Order.Place(
                OrderId.Create(),
                customerId,
                orderNumber,
                shippingAddress,
                billingAddress,
                lineItems,
                utcNow));
    }
}