namespace FluxCommerce.Modules.Catalog.Domain.Errors;

public static class AttributeDefinitionErrors
{
    public static readonly DomainError DeletedAttributeDefinitionCannotBeModified = DomainError.Create(
        "Catalog.DeletedAttributeDefinitionCannotBeModified",
        "A deleted attribute definition cannot be modified.");
}
