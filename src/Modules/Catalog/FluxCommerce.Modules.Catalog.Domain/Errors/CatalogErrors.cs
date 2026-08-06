namespace FluxCommerce.Modules.Catalog.Domain.Errors;

public static class CatalogErrors
{
    public static readonly DomainError InvalidSlug = DomainError.Create(
        "Catalog.InvalidSlug",
        "The slug is invalid.");

    public static readonly DomainError InvalidSku = DomainError.Create(
        "Catalog.InvalidSku",
        "The SKU is invalid.");

    public static readonly DomainError InvalidSeoMetadata = DomainError.Create(
        "Catalog.InvalidSeoMetadata",
        "The SEO metadata is invalid.");
}
