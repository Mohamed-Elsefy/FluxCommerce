using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ActivateCategory;

public sealed record ActivateCategoryCommand(Guid Id) : ICommand<Result>;
