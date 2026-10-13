namespace FluxCommerce.Modules.Catalog.Contracts.Responses;

/// <summary>
/// Aggregate catalog counts for the administrative dashboard overview.
/// Returned as the catalog section of <c>GET /admin/dashboard</c>.
/// </summary>
public sealed record CatalogCountsResponse(
    int TotalProducts,
    int ActiveProducts,
    int TotalCategories,
    int TotalVariants);
