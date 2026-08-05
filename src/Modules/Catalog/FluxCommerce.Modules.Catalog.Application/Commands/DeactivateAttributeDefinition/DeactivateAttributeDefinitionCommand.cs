using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeactivateAttributeDefinition;

public sealed record DeactivateAttributeDefinitionCommand(Guid Id) : ICommand<Result>;
