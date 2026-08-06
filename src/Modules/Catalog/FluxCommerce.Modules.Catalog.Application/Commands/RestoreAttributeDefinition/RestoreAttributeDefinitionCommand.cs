using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.RestoreAttributeDefinition;

public sealed record RestoreAttributeDefinitionCommand(Guid Id) : ICommand<Result>;
