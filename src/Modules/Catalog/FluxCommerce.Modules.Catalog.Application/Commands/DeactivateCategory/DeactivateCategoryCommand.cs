using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeactivateCategory;

public sealed record DeactivateCategoryCommand(Guid Id) : ICommand<Result>;
