namespace FluxCommerce.Modules.Catalog.Domain.Errors;

public static class VariantErrors
{
    public static readonly DomainError DeletedVariantCannotBeModified = DomainError.Create(
        "Catalog.DeletedVariantCannotBeModified",
        "A deleted variant cannot be modified.");
}
