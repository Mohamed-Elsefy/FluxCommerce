using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Contracts.Responses;

namespace FluxCommerce.Modules.Catalog.Application.Queries.GetCatalogCounts;

/// <summary>
/// Returns aggregate catalog counts for the administrative dashboard overview.
/// </summary>
public sealed class GetCatalogCountsQueryHandler
    : IQueryHandler<GetCatalogCountsQuery, Result<CatalogCountsResponse>>
{
    private readonly ICatalogCountsStore _countsStore;

    public GetCatalogCountsQueryHandler(ICatalogCountsStore countsStore)
    {
        _countsStore = countsStore ?? throw new ArgumentNullException(nameof(countsStore));
    }

    public async Task<Result<CatalogCountsResponse>> HandleAsync(
        GetCatalogCountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var counts = await _countsStore
            .GetCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new CatalogCountsResponse(
            counts.TotalProducts,
            counts.ActiveProducts,
            counts.TotalCategories,
            counts.TotalVariants));
    }
}
