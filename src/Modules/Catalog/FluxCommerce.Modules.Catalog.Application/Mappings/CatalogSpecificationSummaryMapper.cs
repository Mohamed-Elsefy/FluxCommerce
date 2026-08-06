using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class CatalogSpecificationSummaryMapper
{
    public static CatalogSpecificationSummary ToSummary(ProductSpecification specification) =>
        new(
            specification.Id,
            specification.Name,
            specification.Value);

    public static IReadOnlyCollection<CatalogSpecificationSummary> ToSummaries(
        IEnumerable<ProductSpecification> specifications) =>
        specifications.Select(ToSummary).ToList();
}
