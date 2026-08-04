namespace FluxCommerce.Modules.Catalog.Domain.Errors;

public static class ProductErrors
{
    public static readonly DomainError InvalidTransition = DomainError.Create(
        "Catalog.InvalidProductTransition",
        "The requested product status transition is not allowed from the current status.");

    public static readonly DomainError DeletedProductCannotBeModified = DomainError.Create(
        "Catalog.DeletedProductCannotBeModified",
        "A deleted product cannot be modified.");
}
