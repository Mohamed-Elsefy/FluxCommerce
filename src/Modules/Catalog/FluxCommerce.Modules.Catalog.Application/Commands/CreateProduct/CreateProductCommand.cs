using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Domain.Enums;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProduct;

public sealed record CreateProductCommand(
    string? Name,
    string? Slug,
    string? Description,
    Guid CategoryId,
    ProductVisibility Visibility,
    SeoMetadataCommand? Seo = null) : ICommand<Result<Guid>>;
