using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeRoleRepository : IRoleRepository
{
    private readonly HashSet<string> _normalizedRoles = new(StringComparer.Ordinal);

    public FakeRoleRepository(IEnumerable<string>? initialRoles = null)
    {
        var roles = initialRoles ?? [Roles.Customer, Roles.Admin, Roles.SuperAdmin];
        foreach (var role in roles)
        {
            var normalized = role.Trim().ToUpperInvariant();
            _normalizedRoles.Add(normalized);
        }
    }

    public Task<IReadOnlyCollection<string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = _normalizedRoles
            .Select(n => n) // already normalized; return as stored normalized
            .ToList()
            .AsReadOnly();
        // Return original casing by mapping back via Roles constants where possible
        var display = _normalizedRoles
            .Select(n => n switch
            {
                "CUSTOMER" => Roles.Customer,
                "ADMIN" => Roles.Admin,
                "SUPERADMIN" => Roles.SuperAdmin,
                _ => n
            })
            .ToList()
            .AsReadOnly();
        return Task.FromResult<IReadOnlyCollection<string>>(display);
    }

    public Task<bool> ExistsAsync(string role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role))
            return Task.FromResult(false);

        var normalized = role.Trim().ToUpperInvariant();
        return Task.FromResult(_normalizedRoles.Contains(normalized));
    }

    public Task AddAsync(string role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("A role name is required.", nameof(role));

        var trimmed = role.Trim();

        if (trimmed.Length > Role.NameMaxLength)
            throw new ArgumentException($"Role name must not exceed {Role.NameMaxLength} characters.", nameof(role));

        var normalized = trimmed.ToUpperInvariant();
        _normalizedRoles.Add(normalized);
        return Task.CompletedTask;
    }
}
