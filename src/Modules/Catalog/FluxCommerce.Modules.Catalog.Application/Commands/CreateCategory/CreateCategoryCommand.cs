using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateCategory;

public sealed record CreateCategoryCommand(
    string? Name,
    string? Slug,
    string? Description,
    Guid? ParentId) : ICommand<Result<Guid>>;
