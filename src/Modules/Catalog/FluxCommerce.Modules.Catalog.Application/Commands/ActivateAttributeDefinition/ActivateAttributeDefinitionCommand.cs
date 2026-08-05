using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ActivateAttributeDefinition;

public sealed record ActivateAttributeDefinitionCommand(Guid Id) : ICommand<Result>;
