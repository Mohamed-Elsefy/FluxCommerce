using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductVariant;

public sealed record UpdateProductVariantCommand(
    Guid Id,
    string? Sku,
    string? Name) : ICommand<Result>;
