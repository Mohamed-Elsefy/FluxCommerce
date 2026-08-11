using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Orders.Contracts.Requests;

/// <summary>
/// Shipping or billing address submitted at Checkout. Constraints mirror the Order address value
/// object limits.
/// </summary>
public sealed record CheckoutAddressRequest(
    [Required][StringLength(100)] string RecipientName,
    [Required][StringLength(200)] string Line1,
    [StringLength(200)] string? Line2,
    [Required][StringLength(100)] string City,
    [Required][StringLength(100)] string Region,
    [Required][StringLength(20)] string PostalCode,
    [Required][StringLength(2, MinimumLength = 2)] string CountryCode);

/// <summary>
/// Request to check out the authenticated customer's Cart. The Cart is identified by the store's
/// opaque Cart id; billing and shipping addresses are captured at Checkout time. The idempotency
/// key is carried in the <c>Idempotency-Key</c> request header, not in this body.
/// </summary>
public sealed record CheckoutRequest(
    [Required] Guid CartId,
    [Required] CheckoutAddressRequest ShippingAddress,
    [Required] CheckoutAddressRequest BillingAddress);