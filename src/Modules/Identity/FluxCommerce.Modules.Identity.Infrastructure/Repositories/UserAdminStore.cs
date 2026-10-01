using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.ReadModels;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Enums;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Identity.Infrastructure.Repositories;

/// <summary>
/// <see cref="IUserAdminStore"/> backed by the Identity DbContext. Reads are served from
/// lightweight projections; no aggregates are tracked. Role membership lives in the user's
/// stored roles column, which cannot be filtered element-wise in the database: when a role
/// filter is supplied the matching identity set is resolved from a lightweight projection
/// before paging so page boundaries stay correct. The identity table is administrative in
/// scale, so the bounded in-memory resolution is an explicit trade-off documented here.
/// </summary>
public sealed class UserAdminStore : IUserAdminStore
{
    private readonly IdentityDbContext _dbContext;

    public UserAdminStore(IdentityDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<PagedResult<UserAdminReadModel>> GetPageAsync(
        UserStatus? status,
        string? role,
        string? search,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = ApplyFilters(_dbContext.Users.AsNoTracking(), status, search);

        if (string.IsNullOrWhiteSpace(role))
        {
            var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

            var rows = await baseQuery
                .OrderByDescending(user => user.RegisteredAtUtc)
                .ThenByDescending(user => user.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(user => new UserAdminRow(
                    user.Id,
                    user.Email.Value,
                    EF.Property<List<string>>(user, "_roles"),
                    user.IsEmailVerified,
                    user.RegisteredAtUtc,
                    user.FirstName,
                    user.LastName,
                    user.PhoneNumber,
                    user.IsPhoneVerified,
                    user.AvatarUrl,
                    user.PreferredLanguage,
                    user.TimeZone,
                    user.Status,
                    user.Lockout.FailedAccessAttemptCount,
                    user.Lockout.LockoutEndUtc))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return new PagedResult<UserAdminReadModel>(
                rows.Select(ToReadModel).ToList(),
                totalCount,
                pageNumber,
                pageSize);
        }

        // Role-filtered path: resolve the matching identity set from a lightweight projection
        // (already ordered newest-first), keep the role matches in that order, then page by
        // identity and fetch only the page's full rows.
        var candidates = await baseQuery
            .OrderByDescending(user => user.RegisteredAtUtc)
            .ThenByDescending(user => user.Id)
            .Select(user => new RoleRow(
                user.Id,
                EF.Property<List<string>>(user, "_roles")))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var matchingIds = candidates
            .Where(candidate => candidate.Roles.Contains(role, StringComparer.Ordinal))
            .Select(candidate => candidate.Id)
            .ToList();

        var total = matchingIds.Count;
        var pageIds = matchingIds
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        if (pageIds.Count == 0)
            return new PagedResult<UserAdminReadModel>([], total, pageNumber, pageSize);

        var pageRows = await _dbContext.Users
            .AsNoTracking()
            .Where(user => pageIds.Contains(user.Id))
            .Select(user => new UserAdminRow(
                user.Id,
                user.Email.Value,
                EF.Property<List<string>>(user, "_roles"),
                user.IsEmailVerified,
                user.RegisteredAtUtc,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.IsPhoneVerified,
                user.AvatarUrl,
                user.PreferredLanguage,
                user.TimeZone,
                user.Status,
                user.Lockout.FailedAccessAttemptCount,
                user.Lockout.LockoutEndUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rowById = pageRows.ToDictionary(row => row.Id);

        var orderedItems = pageIds
            .Select(id => ToReadModel(rowById[id]))
            .ToList();

        return new PagedResult<UserAdminReadModel>(orderedItems, total, pageNumber, pageSize);
    }

    public async Task<UserAdminReadModel?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new UserAdminRow(
                user.Id,
                user.Email.Value,
                EF.Property<List<string>>(user, "_roles"),
                user.IsEmailVerified,
                user.RegisteredAtUtc,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.IsPhoneVerified,
                user.AvatarUrl,
                user.PreferredLanguage,
                user.TimeZone,
                user.Status,
                user.Lockout.FailedAccessAttemptCount,
                user.Lockout.LockoutEndUtc))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToReadModel(row);
    }

    public async Task<UserAdminCountsReadModel> GetCountsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var groups = await _dbContext.Users
            .AsNoTracking()
            .GroupBy(user => user.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = 0;
        var active = 0;
        var suspended = 0;
        var locked = 0;
        var deleted = 0;

        foreach (var group in groups)
        {
            switch (group.Status)
            {
                case UserStatus.Pending:
                    pending = group.Count;
                    break;
                case UserStatus.Active:
                    active = group.Count;
                    break;
                case UserStatus.Suspended:
                    suspended = group.Count;
                    break;
                case UserStatus.Locked:
                    locked = group.Count;
                    break;
                case UserStatus.Deleted:
                    deleted = group.Count;
                    break;
            }
        }

        var unverified = await _dbContext.Users
            .AsNoTracking()
            .CountAsync(user => !user.IsEmailVerified, cancellationToken)
            .ConfigureAwait(false);

        var lockedOut = await _dbContext.Users
            .AsNoTracking()
            .CountAsync(
                user => user.Lockout.LockoutEndUtc != null && user.Lockout.LockoutEndUtc > utcNow,
                cancellationToken)
            .ConfigureAwait(false);

        var roleSets = await _dbContext.Users
            .AsNoTracking()
            .Select(user => EF.Property<List<string>>(user, "_roles"))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new UserAdminCountsReadModel(
            pending + active + suspended + locked + deleted,
            pending,
            active,
            suspended,
            locked,
            deleted,
            roleSets.Count(roles => roles.Contains(Roles.Customer, StringComparer.Ordinal)),
            roleSets.Count(roles => roles.Contains(Roles.Admin, StringComparer.Ordinal)),
            roleSets.Count(roles => roles.Contains(Roles.SuperAdmin, StringComparer.Ordinal)),
            unverified,
            lockedOut);
    }

    private static IQueryable<User> ApplyFilters(
        IQueryable<User> query,
        UserStatus? status,
        string? search)
    {
        if (status.HasValue)
            query = query.Where(user => user.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(user =>
                user.Email.Value.Contains(term) ||
                user.FirstName.Contains(term) ||
                user.LastName.Contains(term));
        }

        return query;
    }

    private static UserAdminReadModel ToReadModel(UserAdminRow row) =>
        new(
            row.Id,
            row.Email,
            row.Roles,
            row.IsEmailVerified,
            row.RegisteredAtUtc,
            row.FirstName,
            row.LastName,
            row.PhoneNumber,
            row.IsPhoneVerified,
            row.AvatarUrl,
            row.PreferredLanguage,
            row.TimeZone,
            row.Status,
            row.FailedAccessAttemptCount,
            row.LockoutEndUtc);

    private sealed record RoleRow(Guid Id, List<string> Roles);

    private sealed record UserAdminRow(
        Guid Id,
        string Email,
        List<string> Roles,
        bool IsEmailVerified,
        DateTime RegisteredAtUtc,
        string FirstName,
        string LastName,
        string? PhoneNumber,
        bool IsPhoneVerified,
        string? AvatarUrl,
        string PreferredLanguage,
        string TimeZone,
        UserStatus Status,
        int FailedAccessAttemptCount,
        DateTime? LockoutEndUtc);
}
