using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariants;

public sealed record CreateProductVariantsCommand(
    Guid ProductId,
    IReadOnlyList<CreateProductVariantItemCommand> Variants) : ICommand<Result<IReadOnlyCollection<Guid>>>;
