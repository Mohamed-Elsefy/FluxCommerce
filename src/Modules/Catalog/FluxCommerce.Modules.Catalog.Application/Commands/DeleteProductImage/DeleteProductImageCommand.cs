using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductImage;

public sealed record DeleteProductImageCommand(
    Guid ProductId,
    Guid ImageId) : ICommand<Result>;
