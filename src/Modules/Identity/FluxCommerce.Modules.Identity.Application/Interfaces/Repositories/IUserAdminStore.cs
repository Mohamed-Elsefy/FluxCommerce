using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.ReadModels;
using FluxCommerce.Modules.Identity.Domain.Enums;

namespace FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;

/// <summary>
/// Persistence driven port for the administrative user management queries. Reads are served
/// from lightweight projections; no aggregates are tracked. Role membership is resolved from
/// the user's stored roles; when a role filter is supplied the matching identity set is
/// resolved before paging so page boundaries stay correct.
/// </summary>
public interface IUserAdminStore
{
    Task<PagedResult<UserAdminReadModel>> GetPageAsync(
        UserStatus? status,
        string? role,
        string? search,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<UserAdminReadModel?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<UserAdminCountsReadModel> GetCountsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
