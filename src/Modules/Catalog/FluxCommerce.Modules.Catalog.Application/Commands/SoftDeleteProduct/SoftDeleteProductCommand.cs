using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteProduct;

public sealed record SoftDeleteProductCommand(Guid Id) : ICommand<Result>;
