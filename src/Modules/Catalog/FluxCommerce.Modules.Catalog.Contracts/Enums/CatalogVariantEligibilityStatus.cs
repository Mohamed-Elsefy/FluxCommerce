namespace FluxCommerce.Modules.Catalog.Contracts.Enums;

/// <summary>
/// Determines whether a Catalog variant may be added to a Cart.
/// </summary>
public enum CatalogVariantEligibilityStatus
{
    Available = 0,
    Missing = 1,
    Unavailable = 2,
}
