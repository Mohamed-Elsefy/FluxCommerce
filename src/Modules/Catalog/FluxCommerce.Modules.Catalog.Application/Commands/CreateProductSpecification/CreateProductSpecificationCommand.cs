using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductSpecification;

public sealed record CreateProductSpecificationCommand(
    Guid ProductId,
    string? Name,
    string? Value) : ICommand<Result>;
