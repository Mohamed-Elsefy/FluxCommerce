namespace FluxCommerce.Modules.Orders.Application.Commands.Checkout;

/// <summary>
/// Raw billing/shipping address data submitted by the client at Checkout. It is mapped to the
/// validated <see cref="FluxCommerce.Modules.Orders.Domain.ValueObjects.OrderAddress"/> value object
/// by the Checkout handler; construction failures surface as a validation error.
/// </summary>
public sealed record CheckoutAddressData(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string CountryCode);