using FluxCommerce.Modules.Catalog.Domain.Enums;

namespace FluxCommerce.Modules.Catalog.Application.Queries.SearchCatalogProducts;

public sealed record CatalogProductFilter(
    Guid? CategoryId = null,
    ProductStatus? Status = null,
    ProductVisibility? Visibility = null,
    bool? IsDeleted = null);
