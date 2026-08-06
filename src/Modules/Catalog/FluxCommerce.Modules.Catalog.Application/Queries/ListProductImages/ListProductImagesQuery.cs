using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProductImages;

public sealed record ListProductImagesQuery(Guid ProductId)
    : IQuery<Result<IReadOnlyCollection<ProductImageResponse>>>;
