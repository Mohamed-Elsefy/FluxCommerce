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

    public static readonly DomainError SpecificationNameRequired = DomainError.Create(
        "Catalog.SpecificationNameRequired",
        "A specification name is required.");

    public static readonly DomainError SpecificationNameTooLong = DomainError.Create(
        "Catalog.SpecificationNameTooLong",
        "The specification name exceeds the maximum allowed length.");

    public static readonly DomainError SpecificationValueRequired = DomainError.Create(
        "Catalog.SpecificationValueRequired",
        "A specification value is required.");

    public static readonly DomainError SpecificationValueTooLong = DomainError.Create(
        "Catalog.SpecificationValueTooLong",
        "The specification value exceeds the maximum allowed length.");

    public static readonly DomainError DuplicateSpecification = DomainError.Create(
        "Catalog.DuplicateProductSpecification",
        "The product already has a specification with this name.");

    public static readonly DomainError SpecificationNotFound = DomainError.Create(
        "Catalog.ProductSpecificationNotFound",
        "The product does not have the specified specification.");

    public static readonly DomainError ImageUrlRequired = DomainError.Create(
        "Catalog.ImageUrlRequired",
        "An image URL is required.");

    public static readonly DomainError ImageUrlTooLong = DomainError.Create(
        "Catalog.ImageUrlTooLong",
        "The image URL exceeds the maximum allowed length.");

    public static readonly DomainError AltTextTooLong = DomainError.Create(
        "Catalog.AltTextTooLong",
        "The image alt text exceeds the maximum allowed length.");

    public static readonly DomainError ProductImageNotFound = DomainError.Create(
        "Catalog.ProductImageNotFound",
        "The product does not have the specified image.");

    public static readonly DomainError InvalidImageOrder = DomainError.Create(
        "Catalog.InvalidImageOrder",
        "The requested image order is invalid.");
}
