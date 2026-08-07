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
}
