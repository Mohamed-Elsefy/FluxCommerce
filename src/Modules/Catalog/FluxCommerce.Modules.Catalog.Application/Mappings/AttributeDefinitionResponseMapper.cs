using FluxCommerce.Modules.Catalog.Contracts.Enums;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Aggregates;

namespace FluxCommerce.Modules.Catalog.Application.Mappings;

public static class AttributeDefinitionResponseMapper
{
    public static AttributeDefinitionResponse ToResponse(AttributeDefinition attributeDefinition) =>
        new(
            attributeDefinition.Id,
            attributeDefinition.Name,
            attributeDefinition.Slug.Value,
            attributeDefinition.Description,
            ToDataType(attributeDefinition.DataType),
            attributeDefinition.IsRequired,
            attributeDefinition.IsFilterable,
            attributeDefinition.IsSearchable,
            attributeDefinition.SortOrder,
            attributeDefinition.CategoryId,
            ToStatus(attributeDefinition.Status),
            attributeDefinition.IsDeleted,
            attributeDefinition.CreatedAtUtc,
            attributeDefinition.DeletedAtUtc);

    public static IReadOnlyCollection<AttributeDefinitionResponse> ToResponses(
        IEnumerable<AttributeDefinition> attributeDefinitions) =>
        attributeDefinitions.Select(ToResponse).ToList();

    private static AttributeStatus ToStatus(Domain.Enums.AttributeStatus status) =>
        status switch
        {
            Domain.Enums.AttributeStatus.Active => AttributeStatus.Active,
            Domain.Enums.AttributeStatus.Inactive => AttributeStatus.Inactive,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

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
