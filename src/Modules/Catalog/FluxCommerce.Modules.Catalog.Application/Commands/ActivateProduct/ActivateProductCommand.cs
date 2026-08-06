using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ActivateProduct;

public sealed record ActivateProductCommand(Guid Id) : ICommand<Result>;
