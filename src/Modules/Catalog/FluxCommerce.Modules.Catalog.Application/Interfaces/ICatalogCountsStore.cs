using FluxCommerce.Modules.Catalog.Application.ReadModels;

namespace FluxCommerce.Modules.Catalog.Application.Interfaces;

/// <summary>
/// Persistence driven port for the administrative catalog overview. Reads are served from
/// lightweight scalar aggregates; no entities are tracked.
/// </summary>
public interface ICatalogCountsStore
{
    Task<CatalogCountsReadModel> GetCountsAsync(CancellationToken cancellationToken = default);
}
