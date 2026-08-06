using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.AssignProductAttributeValue;

public sealed record AssignProductAttributeValueCommand(
    Guid ProductId,
    Guid AttributeDefinitionId,
    string? Value) : ICommand<Result>;
