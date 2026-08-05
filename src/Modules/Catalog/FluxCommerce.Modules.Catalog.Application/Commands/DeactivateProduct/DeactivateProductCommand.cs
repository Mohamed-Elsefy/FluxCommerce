using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeactivateProduct;

public sealed record DeactivateProductCommand(Guid Id) : ICommand<Result>;
