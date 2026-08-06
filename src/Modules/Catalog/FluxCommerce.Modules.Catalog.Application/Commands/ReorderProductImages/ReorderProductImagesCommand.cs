using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ReorderProductImages;

public sealed record ReorderProductImagesCommand(
    Guid ProductId,
    IReadOnlyCollection<Guid> OrderedImageIds) : ICommand<Result>;
