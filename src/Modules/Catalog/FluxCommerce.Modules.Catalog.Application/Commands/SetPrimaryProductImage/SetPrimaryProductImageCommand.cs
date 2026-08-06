using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.SetPrimaryProductImage;

public sealed record SetPrimaryProductImageCommand(
    Guid ProductId,
    Guid ImageId) : ICommand<Result>;
