using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductSpecification;

public sealed record UpdateProductSpecificationCommand(
    Guid ProductId,
    Guid SpecificationId,
    string? Name,
    string? Value) : ICommand<Result>;
