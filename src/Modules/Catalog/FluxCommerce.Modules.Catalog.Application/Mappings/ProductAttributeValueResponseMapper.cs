using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class ProductAttributeValueResponseMapper
{
    public static ProductAttributeValueResponse ToResponse(
        ProductAttributeValue attributeValue,
        AttributeDefinition attributeDefinition,
        Guid productId) =>
        new(
            attributeValue.Id,
            productId,
            attributeValue.AttributeDefinitionId,
            attributeDefinition.Name,
            attributeDefinition.Slug.Value,
            ToDataType(attributeDefinition.DataType),
            attributeValue.Value);

    public static IReadOnlyCollection<ProductAttributeValueResponse> ToResponses(
        Guid productId,
        IEnumerable<ProductAttributeValue> attributeValues,
        IReadOnlyDictionary<Guid, AttributeDefinition> attributeDefinitions) =>
        attributeValues
            .Where(attributeValue => attributeDefinitions.ContainsKey(attributeValue.AttributeDefinitionId))
            .Select(attributeValue => ToResponse(attributeValue, attributeDefinitions[attributeValue.AttributeDefinitionId], productId))
            .ToList();

    private static AttributeDataType ToDataType(Domain.Enums.AttributeDataType dataType) =>
        dataType switch
        {
            Domain.Enums.AttributeDataType.Text => AttributeDataType.Text,
            Domain.Enums.AttributeDataType.Number => AttributeDataType.Number,
            Domain.Enums.AttributeDataType.Decimal => AttributeDataType.Decimal,
            Domain.Enums.AttributeDataType.Boolean => AttributeDataType.Boolean,
            Domain.Enums.AttributeDataType.Date => AttributeDataType.Date,
            Domain.Enums.AttributeDataType.DateTime => AttributeDataType.DateTime,
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null)
        };
}
