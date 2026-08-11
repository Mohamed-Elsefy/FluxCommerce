using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.Modules.Orders.Application.Errors;

/// <summary>
/// Application-level errors raised while verifying, assembling and persisting the Checkout Order
/// snapshot (Cart ownership, Catalog revalidation, address validation and idempotency).
/// </summary>
public static class CheckoutErrors
{
    public static readonly Error AuthenticationRequired = Error.Unauthorized(
        "Order.AuthenticationRequired",
        "Authentication is required to place an order.");

    public static readonly Error CartNotFound = Error.NotFound(
        "Order.CartNotFound",
        "The cart was not found.");

    public static readonly Error CartAccessDenied = Error.Forbidden(
        "Order.CartAccessDenied",
        "The cart does not belong to the current user.");

    public static readonly Error InvalidIdempotencyKey = Error.Validation(
        "Order.InvalidIdempotencyKey",
        "A valid Idempotency-Key header is required.");

    public static readonly Error IdempotencyConflict = Error.Conflict(
        "Order.IdempotencyConflict",
        "The Idempotency-Key was already used for a different checkout request.");

    public static readonly Error CartEmpty = Error.Validation(
        "Order.CartEmpty",
        "The cart contains no items to check out.");

    public static readonly Error VariantUnavailable = Error.Conflict(
        "Order.VariantUnavailable",
        "One or more cart items is no longer available for purchase.");

    public static readonly Error DuplicateVariant = Error.Conflict(
        "Order.DuplicateVariant",
        "The order cannot contain the same variant more than once.");

    public static readonly Error CurrencyMismatch = Error.Conflict(
        "Order.CurrencyMismatch",
        "All money amounts on an order must share the same currency.");

    public static readonly Error InvalidAddress = Error.Validation(
        "Order.InvalidAddress",
        "The checkout address data is invalid.");
}