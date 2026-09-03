using FluxCommerce.Modules.Catalog.Contracts.ReadModels;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class CatalogAttributeSummaryMapper
{
    public static CatalogAttributeSummary ToSummary(
        ProductAttributeValue attributeValue,
        AttributeDefinition attributeDefinition) =>
        new(
            attributeValue.AttributeDefinitionId,
            attributeDefinition.Name,
            attributeDefinition.Slug.Value,
            ProductAttributeValueResponseMapper.ToDataType(attributeDefinition.DataType),
            attributeValue.Value);

    public static IReadOnlyCollection<CatalogAttributeSummary> ToSummaries(
        IEnumerable<ProductAttributeValue> attributeValues,
        IReadOnlyDictionary<Guid, AttributeDefinition> attributeDefinitions) =>
        attributeValues
            .Where(attributeValue => attributeDefinitions.ContainsKey(attributeValue.AttributeDefinitionId))
            .Select(attributeValue => ToSummary(attributeValue, attributeDefinitions[attributeValue.AttributeDefinitionId]))
            .ToList();
}
