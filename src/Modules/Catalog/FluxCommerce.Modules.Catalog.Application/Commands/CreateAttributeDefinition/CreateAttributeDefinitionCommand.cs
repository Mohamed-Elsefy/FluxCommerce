using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Domain.Enums;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateAttributeDefinition;

public sealed record CreateAttributeDefinitionCommand(
    string? Name,
    string? Slug,
    string? Description,
    AttributeDataType DataType,
    bool IsRequired,
    bool IsFilterable,
    bool IsSearchable,
    int SortOrder,
    Guid? CategoryId) : ICommand<Result<Guid>>;
