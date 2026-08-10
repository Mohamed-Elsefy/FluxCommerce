namespace FluxCommerce.Modules.Orders.Domain.Errors;

public static class OrderErrors
{
    public static readonly DomainError InvalidQuantity = DomainError.Create(
        "Order.InvalidQuantity",
        "An order line item quantity must be a positive integer.");

    public static readonly DomainError QuantityLimitExceeded = DomainError.Create(
        "Order.QuantityLimitExceeded",
        "The order line item quantity exceeds the maximum allowed.");

    public static readonly DomainError DuplicateVariant = DomainError.Create(
        "Order.DuplicateVariant",
        "The order already contains a line item for the specified variant.");

    public static readonly DomainError LineItemNotFound = DomainError.Create(
        "Order.LineItemNotFound",
        "The order does not contain a line item for the specified variant.");

    public static readonly DomainError EmptyOrder = DomainError.Create(
        "Order.EmptyOrder",
        "An order must contain at least one line item.");

    public static readonly DomainError CurrencyMismatch = DomainError.Create(
        "Order.CurrencyMismatch",
        "All money amounts on an order must share the same currency.");

    public static readonly DomainError InvalidStateTransition = DomainError.Create(
        "Order.InvalidStateTransition",
        "The order cannot transition to the requested state from its current state.");

    public static readonly DomainError InvalidOrderNumber = DomainError.Create(
        "Order.InvalidOrderNumber",
        "A valid order number is required.");

    public static readonly DomainError InvalidAddress = DomainError.Create(
        "Order.InvalidAddress",
        "A valid shipping and billing address is required.");

    public static readonly DomainError InvalidCustomer = DomainError.Create(
        "Order.InvalidCustomer",
        "A valid customer is required.");

    public static readonly DomainError OrderNotFound = DomainError.Create(
        "Order.NotFound",
        "The order was not found.");
}
