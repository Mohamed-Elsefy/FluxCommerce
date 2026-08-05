namespace FluxCommerce.Modules.Catalog.Domain.Errors;

public static class ProductErrors
{
    public static readonly DomainError InvalidTransition = DomainError.Create(
        "Catalog.InvalidProductTransition",
        "The requested product status transition is not allowed from the current status.");

    public static readonly DomainError DeletedProductCannotBeModified = DomainError.Create(
        "Catalog.DeletedProductCannotBeModified",
        "A deleted product cannot be modified.");

    public static readonly DomainError AttributeValueRequired = DomainError.Create(
        "Catalog.AttributeValueRequired",
        "An attribute value is required.");

    public static readonly DomainError AttributeValueTooLong = DomainError.Create(
        "Catalog.AttributeValueTooLong",
        "The attribute value exceeds the maximum allowed length.");

    public static readonly DomainError InvalidAttributeValue = DomainError.Create(
        "Catalog.InvalidAttributeValue",
        "The attribute value is not valid for the attribute's data type.");

    public static readonly DomainError DuplicateAttributeValue = DomainError.Create(
        "Catalog.DuplicateProductAttributeValue",
        "The product already has a value for the specified attribute.");

    public static readonly DomainError AttributeValueNotFound = DomainError.Create(
        "Catalog.ProductAttributeValueNotFound",
        "The product does not have a value for the specified attribute.");

    public static readonly DomainError DeletedAttributeDefinitionCannotBeUsed = DomainError.Create(
        "Catalog.DeletedAttributeDefinitionCannotBeUsed",
        "The attribute definition is deleted and cannot be used.");

    public static readonly DomainError InactiveAttributeDefinitionCannotBeUsed = DomainError.Create(
        "Catalog.AttributeDefinitionNotActive",
        "The attribute definition is not active and cannot be used.");

    public static readonly DomainError AttributeDefinitionCategoryMismatch = DomainError.Create(
        "Catalog.AttributeDefinitionCategoryMismatch",
        "The attribute definition does not belong to the product's category.");
}
