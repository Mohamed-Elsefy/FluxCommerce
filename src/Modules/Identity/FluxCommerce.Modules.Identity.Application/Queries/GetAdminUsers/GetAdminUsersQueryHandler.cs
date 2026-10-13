using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Application.Validation;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Enums;

namespace FluxCommerce.Modules.Identity.Application.Queries.GetAdminUsers;

/// <summary>
/// Administrative user list: validates pagination and the optional filters, then reads
/// from the lightweight projection. Authorization is enforced at the API boundary
/// (<c>SuperAdminPolicy</c>).
/// </summary>
public sealed class GetAdminUsersQueryHandler
    : IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>>
{
    private readonly IUserAdminStore _adminStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetAdminUsersQueryHandler(IUserAdminStore adminStore, IDateTimeProvider dateTimeProvider)
    {
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<PagedResult<UserAdminResponse>>> HandleAsync(
        GetAdminUsersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageNumber = query.PageNumber ?? UserAdminPaginationRules.DefaultPageNumber;
        var pageSize = query.PageSize ?? UserAdminPaginationRules.DefaultPageSize;

        var validationError = UserAdminPaginationRules.Validate(pageNumber, pageSize);
        if (validationError is not null)
            return Result.Failure<PagedResult<UserAdminResponse>>(validationError);

        UserStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<UserStatus>(query.Status.Trim(), ignoreCase: true, out var parsed)
                || !Enum.IsDefined(typeof(UserStatus), parsed))
                return Result.Failure<PagedResult<UserAdminResponse>>(
                    Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

            status = parsed;
        }

        string? role = null;
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var trimmed = query.Role.Trim();
            role = ResolveRole(trimmed);
            if (role is null)
                return Result.Failure<PagedResult<UserAdminResponse>>(
                    Error.Validation("Identity.InvalidRole", "The specified role is invalid."));
        }

        var utcNow = _dateTimeProvider.UtcNow;

        var result = await _adminStore
            .GetPageAsync(status, role, query.Search, pageNumber, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new PagedResult<UserAdminResponse>(
            result.Items.Select(user => UserAdminResponseMapper.ToResponse(user, utcNow)).ToList(),
            result.TotalCount,
            result.PageNumber,
            result.PageSize));
    }

    private static string? ResolveRole(string role)
    {
        if (string.Equals(role, Roles.Customer, StringComparison.OrdinalIgnoreCase))
            return Roles.Customer;

        if (string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase))
            return Roles.Admin;

        if (string.Equals(role, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            return Roles.SuperAdmin;

        return null;
    }
}
