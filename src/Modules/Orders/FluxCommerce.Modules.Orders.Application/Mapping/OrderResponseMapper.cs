using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Mapping;

/// <summary>
/// Maps Orders aggregates and read models to the module's public response contracts.
/// </summary>
public static class OrderResponseMapper
{
    public static OrderSummaryResponse ToSummary(OrderSummaryReadModel order) =>
        new(
            order.OrderId.Value,
            order.OrderNumber.Value,
            order.Status.ToString(),
            new OrderMoneyResponse(order.GrandTotalAmount, order.GrandTotalCurrency),
            order.ItemCount,
            order.CreatedAtUtc);

    public static IReadOnlyList<OrderSummaryResponse> ToSummaries(
        IEnumerable<OrderSummaryReadModel> orders) =>
        orders.Select(ToSummary).ToList();

    public static OrderDetailsResponse ToDetails(Order order)
    {
        var lineItems = order.LineItems
            .Select(item => new OrderLineItemResponse(
                item.ProductName,
                item.Sku.Value,
                item.Quantity,
                item.UnitPrice.Amount,
                item.UnitPrice.Currency,
                item.LineTotal.Amount,
                item.LineTotal.Currency))
            .ToList();

        return new OrderDetailsResponse(
            order.Id.Value,
            order.OrderNumber.Value,
            order.Status.ToString(),
            Money(order.Subtotal),
            Money(order.ShippingCost),
            Money(order.Discount),
            Money(order.GrandTotal),
            order.LineItems.Count,
            order.CreatedAtUtc,
            Address(order.ShippingAddress),
            Address(order.BillingAddress),
            lineItems);
    }

    private static OrderMoneyResponse Money(OrderMoney money) =>
        new(money.Amount, money.Currency);

    private static OrderAddressResponse Address(OrderAddress address) =>
        new(
            address.RecipientName,
            address.Line1,
            address.Line2,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode);
}
