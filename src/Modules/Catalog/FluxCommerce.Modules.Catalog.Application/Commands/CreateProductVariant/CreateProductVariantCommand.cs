using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariant;

public sealed record CreateProductVariantCommand(
    Guid ProductId,
    string? Sku,
    string? Name,
    decimal UnitPriceAmount,
    string Currency) : ICommand<Result<Guid>>;
