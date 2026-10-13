using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetAdminUser;

/// <summary>
/// Loads the requested user by identity and maps it to the administrative response.
/// A missing user surfaces as not-found.
/// </summary>
public sealed class GetAdminUserQueryHandler : IQueryHandler<GetAdminUserQuery, Result<UserAdminResponse>>
{
    private readonly IUserAdminStore _adminStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetAdminUserQueryHandler(IUserAdminStore adminStore, IDateTimeProvider dateTimeProvider)
    {
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<UserAdminResponse>> HandleAsync(
        GetAdminUserQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await _adminStore
            .GetByIdAsync(query.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
            return Result.Failure<UserAdminResponse>(
                Error.NotFound("Identity.UserNotFound", "The user account was not found."));

        return Result.Success(UserAdminResponseMapper.ToResponse(user, _dateTimeProvider.UtcNow));
    }
}
