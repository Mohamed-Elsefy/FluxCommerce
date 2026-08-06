using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.AddProductImage;

public sealed record AddProductImageCommand(
    Guid ProductId,
    string? ImageUrl,
    string? AltText,
    bool IsPrimary,
    int DisplayOrder,
    Dictionary<string, string>? Metadata) : ICommand<Result>;
