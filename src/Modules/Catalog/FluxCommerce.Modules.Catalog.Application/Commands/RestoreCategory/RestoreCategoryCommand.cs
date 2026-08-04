using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.RestoreCategory;

public sealed record RestoreCategoryCommand(Guid Id) : ICommand<Result>;
