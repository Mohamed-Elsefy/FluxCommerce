using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetPermissionCatalog;

public sealed class GetPermissionCatalogQueryHandler
    : IQueryHandler<GetPermissionCatalogQuery, Result<IReadOnlyCollection<string>>>
{
    public Task<Result<IReadOnlyCollection<string>>> HandleAsync(
        GetPermissionCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(Result.Success(PermissionCatalog.All));
    }
}
