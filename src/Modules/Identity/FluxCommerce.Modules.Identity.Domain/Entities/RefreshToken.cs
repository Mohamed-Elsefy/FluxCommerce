using FluxCommerce.BuildingBlocks.Domain.Entities;

namespace FluxCommerce.Modules.Identity.Domain.Entities;

public sealed class RefreshToken : Entity<Guid>
{
    public Guid UserId { get; }

    public string Value { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime ExpiresAtUtc { get; }

    public DateTime? RevokedAtUtc { get; private set; }

    public string? ReplacedByTokenValue { get; private set; }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public RefreshToken(Guid id, Guid userId, string value, DateTime createdAtUtc, DateTime expiresAtUtc)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A refresh token id must be provided.", nameof(id));

        if (userId == Guid.Empty)
            throw new ArgumentException("A user id must be provided.", nameof(userId));

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A refresh token value is required.", nameof(value));

        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Refresh token expiry must be after its creation.", nameof(expiresAtUtc));

        UserId = userId;
        Value = value;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool Matches(string tokenValue) =>
        string.Equals(Value, tokenValue, StringComparison.Ordinal);

    public bool IsExpiredAt(DateTime utcNow) =>
        utcNow >= ExpiresAtUtc;

    public bool IsActiveAt(DateTime utcNow) =>
        !IsRevoked && !IsExpiredAt(utcNow);

    public void Revoke(DateTime utcNow, string? replacedByTokenValue)
    {
        if (IsRevoked)
            throw new InvalidOperationException("The refresh token has already been revoked.");

        RevokedAtUtc = utcNow;
        ReplacedByTokenValue = replacedByTokenValue;
    }
}
