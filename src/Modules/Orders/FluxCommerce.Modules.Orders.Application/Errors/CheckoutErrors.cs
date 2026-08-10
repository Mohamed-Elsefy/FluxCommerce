using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.Modules.Orders.Application.Errors;

/// <summary>
/// Application-level errors raised while assembling the Checkout Order snapshot from the Cart snapshot
/// and the revalidated Catalog data (Phase 4.6).
/// </summary>
public static class CheckoutErrors
{
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
}