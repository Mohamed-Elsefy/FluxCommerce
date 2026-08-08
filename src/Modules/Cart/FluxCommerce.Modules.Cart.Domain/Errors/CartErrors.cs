namespace FluxCommerce.Modules.Cart.Domain.Errors;

public static class CartErrors
{
    public static readonly DomainError InvalidQuantity = DomainError.Create(
        "Cart.InvalidQuantity",
        "A cart item quantity must be a positive integer.");

    public static readonly DomainError QuantityLimitExceeded = DomainError.Create(
        "Cart.QuantityLimitExceeded",
        "The cart item quantity exceeds the maximum allowed.");

    public static readonly DomainError CartItemNotFound = DomainError.Create(
        "Cart.CartItemNotFound",
        "The cart does not contain an item for the specified variant.");

    public static readonly DomainError InvalidMergeSource = DomainError.Create(
        "Cart.InvalidMergeSource",
        "Only a guest cart can be used as the source of a cart merge.");

    public static readonly DomainError CartConcurrencyConflict = DomainError.Create(
        "Cart.CartConcurrencyConflict",
        "The cart was modified by another request. Reload the cart and try again.");

    public static readonly DomainError CartNotFound = DomainError.Create(
        "Cart.CartNotFound",
        "The cart was not found.");

    public static readonly DomainError CartAccessDenied = DomainError.Create(
        "Cart.CartAccessDenied",
        "The cart is not accessible with the provided identifier.");

    public static readonly DomainError InvalidCartId = DomainError.Create(
        "Cart.InvalidCartId",
        "A valid cart identifier is required.");

    public static readonly DomainError InvalidCartItem = DomainError.Create(
        "Cart.InvalidCartItem",
        "The cart item data is invalid.");

    public static readonly DomainError AuthenticationRequired = DomainError.Create(
        "Cart.AuthenticationRequired",
        "Authentication is required to access a user cart.");

    public static readonly DomainError VariantNotFound = DomainError.Create(
        "Cart.VariantNotFound",
        "The variant was not found.");

    public static readonly DomainError VariantUnavailable = DomainError.Create(
        "Cart.VariantUnavailable",
        "The variant is not available for purchase.");

    public static readonly DomainError CatalogOperationFailed = DomainError.Create(
        "Cart.CatalogOperationFailed",
        "The variant could not be validated. Try again.");
}
