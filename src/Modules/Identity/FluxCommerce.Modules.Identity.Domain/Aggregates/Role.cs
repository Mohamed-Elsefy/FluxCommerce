using FluxCommerce.BuildingBlocks.Domain.Entities;

namespace FluxCommerce.Modules.Identity.Domain.Aggregates;

/// <summary>
/// Phase A — Real Role persistence (lookup/validation store).
/// `Roles` table is the source of truth for `IRoleRepository.ExistsAsync()` and listing,
/// while `User.Roles` string list remains the per-user assignment (dual source intentional for fixed roles).
/// `NormalizedName` is the unique key (upper-invariant) to enforce case-insensitive uniqueness.
/// </summary>
public sealed class Role : Entity<Guid>
{
    public const int NameMaxLength = 64;

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Role(Guid id, string name, string normalizedName, DateTimeOffset createdAtUtc)
        : base(id)
    {
        Name = name;
        NormalizedName = normalizedName;
        CreatedAtUtc = createdAtUtc;
    }

#pragma warning disable CS8618
    private Role() { }
#pragma warning restore CS8618

    public static Role Create(string name, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A role name is required.", nameof(name));

        var trimmed = name.Trim();

        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Role name must not exceed {NameMaxLength} characters.", nameof(name));

        if (createdAtUtc == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(createdAtUtc));

        var normalized = trimmed.ToUpperInvariant();

        return new Role(Guid.NewGuid(), trimmed, normalized, createdAtUtc);
    }

    public static Role CreateWithId(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A role id must be provided.", nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A role name is required.", nameof(name));

        var trimmed = name.Trim();

        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Role name must not exceed {NameMaxLength} characters.", nameof(name));

        if (createdAtUtc == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(createdAtUtc));

        var normalized = trimmed.ToUpperInvariant();

        return new Role(id, trimmed, normalized, createdAtUtc);
    }
}
