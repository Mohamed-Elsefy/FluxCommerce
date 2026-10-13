using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetAdminUserCounts;

/// <summary>
/// Returns aggregate user counts for the administrative overview.
/// </summary>
public sealed class GetAdminUserCountsQueryHandler
    : IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>>
{
    private readonly IUserAdminStore _adminStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetAdminUserCountsQueryHandler(IUserAdminStore adminStore, IDateTimeProvider dateTimeProvider)
    {
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<UserAdminCountsResponse>> HandleAsync(
        GetAdminUserCountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var counts = await _adminStore
            .GetCountsAsync(_dateTimeProvider.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(UserAdminResponseMapper.ToResponse(counts));
    }
}
