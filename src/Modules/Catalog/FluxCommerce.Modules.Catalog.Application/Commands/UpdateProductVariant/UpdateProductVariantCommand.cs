using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Domain.Enums;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductVariant;

public sealed record UpdateProductVariantCommand(
    Guid Id,
    string? Sku,
    string? Name,
    decimal? UnitPriceAmount = null,
    string? Currency = null,
    VariantStatus? Status = null) : ICommand<Result>;
