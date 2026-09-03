namespace FluxCommerce.Modules.Catalog.Domain.Errors;

public static class CategoryErrors
{
    public static readonly DomainError CannotBeOwnParent = DomainError.Create(
        "Catalog.CannotBeOwnParent",
        "A category cannot be its own parent.");

    public static readonly DomainError DeletedCategoryCannotBeModified = DomainError.Create(
        "Catalog.DeletedCategoryCannotBeModified",
        "A deleted category cannot be modified.");
}
