using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class CatalogVariantSummaryMapper
{
    public static CatalogVariantSummary ToSummary(Variant variant) =>
        new(
            variant.Id,
            variant.Sku.Value,
            variant.Name,
            variant.UnitPrice.Amount,
            variant.UnitPrice.Currency);

    public static IReadOnlyCollection<CatalogVariantSummary> ToSummaries(
        IEnumerable<Variant> variants) =>
        variants.Select(ToSummary).ToList();
}
