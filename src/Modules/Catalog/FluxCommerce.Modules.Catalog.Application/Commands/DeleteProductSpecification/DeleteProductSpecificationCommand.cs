using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductSpecification;

public sealed record DeleteProductSpecificationCommand(
    Guid ProductId,
    Guid SpecificationId) : ICommand<Result>;
