using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private static readonly IReadOnlyCollection<string> BuiltInRoles =
        [Roles.Customer, Roles.Admin, Roles.SuperAdmin];

    public Task<IReadOnlyCollection<string>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BuiltInRoles);

    public Task<bool> ExistsAsync(string role, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuiltInRoles.Contains(role));

    public Task AddAsync(string role, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
