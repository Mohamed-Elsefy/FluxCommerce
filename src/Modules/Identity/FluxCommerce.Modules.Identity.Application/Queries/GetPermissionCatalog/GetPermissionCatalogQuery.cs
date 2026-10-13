using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetPermissionCatalog;

/// <summary>
/// Reads the action-level permission catalog (single source of truth in the Identity Domain).
/// Static data; no authorization beyond the API boundary (<c>SuperAdminPolicy</c>).
/// </summary>
public sealed record GetPermissionCatalogQuery
    : IQuery<Result<IReadOnlyCollection<string>>>;
