using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.RemoveProductAttributeValue;

public sealed record RemoveProductAttributeValueCommand(
    Guid ProductId,
    Guid AttributeDefinitionId) : ICommand<Result>;
