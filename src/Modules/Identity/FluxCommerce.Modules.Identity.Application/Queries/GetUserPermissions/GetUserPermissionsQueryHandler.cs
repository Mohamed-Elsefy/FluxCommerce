using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetUserPermissions;

public sealed class GetUserPermissionsQueryHandler
    : IQueryHandler<GetUserPermissionsQuery, Result<IReadOnlyCollection<string>>>
{
    private readonly IUserRepository _userRepository;

    public GetUserPermissionsQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
    }

    public async Task<Result<IReadOnlyCollection<string>>> HandleAsync(
        GetUserPermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await _userRepository.GetByIdAsync(query.UserId, cancellationToken);

        if (user is null)
            return Result.Failure<IReadOnlyCollection<string>>(
                Error.NotFound("Identity.UserNotFound", "The user account was not found."));

        return Result.Success(user.Permissions);
    }
}
