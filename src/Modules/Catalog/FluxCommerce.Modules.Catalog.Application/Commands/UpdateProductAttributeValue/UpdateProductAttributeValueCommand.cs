using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductAttributeValue;

public sealed record UpdateProductAttributeValueCommand(
    Guid ProductId,
    Guid AttributeDefinitionId,
    string? Value) : ICommand<Result>;
