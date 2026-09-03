using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteCategory;

public sealed record SoftDeleteCategoryCommand(Guid Id) : ICommand<Result>;
