using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class CatalogProductListItemMapper
{
    public static CatalogProductListItem ToListItem(Product product) =>
        new(
            product.Id,
            product.Name,
            product.Slug.Value,
            product.Description,
            product.CategoryId,
            product.Images
                .Where(image => image.IsPrimary)
                .OrderBy(image => image.DisplayOrder)
                .Select(CatalogImageSummaryMapper.ToSummary)
                .FirstOrDefault());

    public static IReadOnlyCollection<CatalogProductListItem> ToListItems(
        IEnumerable<Product> products) =>
        products.Select(ToListItem).ToList();
}
