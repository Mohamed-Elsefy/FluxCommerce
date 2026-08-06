using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ArchiveProduct;

public sealed record ArchiveProductCommand(Guid Id) : ICommand<Result>;
