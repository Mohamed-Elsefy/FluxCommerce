using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateCategory;

public sealed record UpdateCategoryCommand(
    Guid Id,
    string? Name,
    string? Slug,
    string? Description,
    Guid? ParentId) : ICommand<Result>;
