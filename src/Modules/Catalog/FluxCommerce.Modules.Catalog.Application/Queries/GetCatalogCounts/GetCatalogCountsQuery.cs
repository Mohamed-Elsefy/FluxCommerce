using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetCatalogCounts;

/// <summary>
/// Returns aggregate catalog counts for the administrative dashboard overview.
/// </summary>
public sealed record GetCatalogCountsQuery
    : IQuery<Result<CatalogCountsResponse>>;
