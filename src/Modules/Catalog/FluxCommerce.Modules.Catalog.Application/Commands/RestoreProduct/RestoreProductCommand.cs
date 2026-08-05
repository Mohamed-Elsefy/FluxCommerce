using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.RestoreProduct;

public sealed record RestoreProductCommand(Guid Id) : ICommand<Result>;
