using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class CatalogCategoryTreeMapper
{
    public static IReadOnlyCollection<CatalogCategoryTree> ToTree(
        IEnumerable<Category> categories)
    {
        var lookup = categories.ToLookup(category => category.ParentId);

        return BuildChildren(lookup, null);
    }

    private static IReadOnlyCollection<CatalogCategoryTree> BuildChildren(
        ILookup<Guid?, Category> lookup,
        Guid? parentId) =>
        lookup[parentId]
            .OrderBy(category => category.Name)
            .Select(category => new CatalogCategoryTree(
                category.Id,
                category.Name,
                category.Slug.Value,
                category.Description,
                category.ParentId,
                BuildChildren(lookup, category.Id)))
            .ToList();
}
