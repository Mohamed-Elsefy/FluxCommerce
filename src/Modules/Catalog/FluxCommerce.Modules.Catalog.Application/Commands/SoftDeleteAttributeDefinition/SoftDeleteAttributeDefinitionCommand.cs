using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteAttributeDefinition;

public sealed record SoftDeleteAttributeDefinitionCommand(Guid Id) : ICommand<Result>;
