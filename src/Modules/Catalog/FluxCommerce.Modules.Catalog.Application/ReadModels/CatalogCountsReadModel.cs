namespace FluxCommerce.Modules.Catalog.Application.ReadModels;

/// <summary>
/// Aggregate catalog counts for the administrative dashboard overview. Totals exclude
/// soft-deleted rows; <c>ActiveProducts</c> further requires the active lifecycle status.
/// </summary>
public sealed record CatalogCountsReadModel(
    int TotalProducts,
    int ActiveProducts,
    int TotalCategories,
    int TotalVariants);
