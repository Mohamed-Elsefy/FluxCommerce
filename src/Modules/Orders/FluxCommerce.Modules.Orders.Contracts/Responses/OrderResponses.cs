namespace FluxCommerce.Modules.Orders.Contracts.Responses;

/// <summary>
/// Money snapshot returned on an Order, preserving the amount and its three-letter ISO 4217 currency.
/// </summary>
public sealed record OrderMoneyResponse(decimal Amount, string Currency);

/// <summary>
/// Address snapshot captured on an Order at checkout time.
/// </summary>
public sealed record OrderAddressResponse(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string CountryCode);

/// <summary>
/// A single immutable line item on an Order, with its unit price and computed line total.
/// </summary>
public sealed record OrderLineItemResponse(
    string ProductName,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    string UnitPriceCurrency,
    decimal LineTotal,
    string LineTotalCurrency);

/// <summary>
/// Lightweight Order list entry used by <c>GET /orders</c>.
/// </summary>
public sealed record OrderSummaryResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    OrderMoneyResponse GrandTotal,
    int ItemCount,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Full Order representation returned by <c>GET /orders/{orderId}</c> and the cancel endpoint.
/// </summary>
public sealed record OrderDetailsResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    OrderMoneyResponse Subtotal,
    OrderMoneyResponse ShippingCost,
    OrderMoneyResponse Discount,
    OrderMoneyResponse GrandTotal,
    int ItemCount,
    DateTimeOffset CreatedAtUtc,
    OrderAddressResponse ShippingAddress,
    OrderAddressResponse BillingAddress,
    IReadOnlyList<OrderLineItemResponse> LineItems);
