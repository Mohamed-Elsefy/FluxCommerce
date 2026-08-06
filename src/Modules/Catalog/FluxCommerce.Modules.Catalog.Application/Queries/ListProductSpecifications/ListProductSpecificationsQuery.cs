using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProductSpecifications;

public sealed record ListProductSpecificationsQuery(Guid ProductId)
    : IQuery<Result<IReadOnlyCollection<ProductSpecificationResponse>>>;
