using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductVariant;

public sealed record DeleteProductVariantCommand(Guid Id) : ICommand<Result>;
