namespace FluxCommerce.Api.Localization;

public sealed partial class ErrorCodeLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> CartEntries =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Cart.InvalidQuantity"] = "Cart.InvalidQuantity",
            ["Cart.QuantityLimitExceeded"] = "Cart.QuantityLimitExceeded",
            ["Cart.CartItemNotFound"] = "Cart.CartItemNotFound",
            ["Cart.CartNotFound"] = "Cart.CartNotFound",
            ["Cart.CartAccessDenied"] = "Cart.CartAccessDenied",
            ["Cart.CartConcurrencyConflict"] = "Cart.CartConcurrencyConflict",
            ["Cart.InvalidCartItem"] = "Cart.InvalidCartItem",
            ["Cart.InvalidCartId"] = "Cart.InvalidCartId",
            ["Cart.AuthenticationRequired"] = "Cart.AuthenticationRequired",
            ["Cart.VariantNotFound"] = "Cart.VariantNotFound",
            ["Cart.VariantUnavailable"] = "Cart.VariantUnavailable",
            ["Cart.CatalogOperationFailed"] = "Cart.CatalogOperationFailed",
        };

    private static readonly IReadOnlyDictionary<string, string> OrderEntries =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Order.CartEmpty"] = "Order.CartEmpty",
            ["Order.VariantUnavailable"] = "Order.VariantUnavailable",
            ["Order.DuplicateVariant"] = "Order.DuplicateVariant",
            ["Order.CurrencyMismatch"] = "Order.CurrencyMismatch",
            ["Order.AuthenticationRequired"] = "Order.AuthenticationRequired",
            ["Order.CartNotFound"] = "Order.CartNotFound",
            ["Order.CartAccessDenied"] = "Order.CartAccessDenied",
            ["Order.InvalidIdempotencyKey"] = "Order.InvalidIdempotencyKey",
            ["Order.IdempotencyConflict"] = "Order.IdempotencyConflict",
            ["Order.InvalidAddress"] = "Order.InvalidAddress",
            ["Order.QueryAuthenticationRequired"] = "Authorization.Unauthorized",
            ["Order.ValidationFailed"] = "Errors.RequestValidation",
            ["Order.NotFound"] = "Errors.NotFound",
            ["Order.CancelConflict"] = "Order.CancelConflict",
        };

    private static readonly IReadOnlyDictionary<string, string> InventoryEntries =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Inventory.InvalidSku"] = "Inventory.InvalidSku",
            ["Inventory.InvalidQuantity"] = "Inventory.InvalidQuantity",
            ["Inventory.SkuNotFound"] = "Errors.NotFound",
            ["Inventory.DuplicateSku"] = "Inventory.DuplicateSku",
            ["Inventory.InvalidReservationId"] = "Inventory.InvalidReservationId",
            ["Inventory.ReservationNotFound"] = "Errors.NotFound",
        };
}
