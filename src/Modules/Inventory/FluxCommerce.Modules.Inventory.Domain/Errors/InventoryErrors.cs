namespace FluxCommerce.Modules.Inventory.Domain.Errors;

public static class InventoryErrors
{
    public static readonly DomainError InvalidQuantity = DomainError.Create(
        "Inventory.InvalidQuantity",
        "An inventory quantity must be a positive integer.");

    public static readonly DomainError InsufficientStock = DomainError.Create(
        "Inventory.InsufficientStock",
        "There is not enough available stock to satisfy the requested quantity.");

    public static readonly DomainError ReleaseExceedsReserved = DomainError.Create(
        "Inventory.ReleaseExceedsReserved",
        "A release cannot return more quantity than is currently reserved.");

    public static readonly DomainError CommitExceedsReserved = DomainError.Create(
        "Inventory.CommitExceedsReserved",
        "A commit cannot consume more quantity than is currently reserved.");

    public static readonly DomainError InvalidExpiration = DomainError.Create(
        "Inventory.InvalidExpiration",
        "A reservation expiration must be in the future.");

    public static readonly DomainError InvalidStateTransition = DomainError.Create(
        "Inventory.InvalidStateTransition",
        "The reservation cannot transition to the requested state from its current state.");
}
