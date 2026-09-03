using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductImage;

public sealed record UpdateProductImageCommand(
    Guid ProductId,
    Guid ImageId,
    string? ImageUrl,
    string? AltText,
    int DisplayOrder,
    Dictionary<string, string>? Metadata) : ICommand<Result>;
