using FluxCommerce.BuildingBlocks.Domain.Entities;

namespace FluxCommerce.Modules.Identity.Domain.Entities;

public sealed class EmailVerificationToken : Entity<Guid>
{
    public Guid UserId { get; }

    public string Value { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime ExpiresAtUtc { get; }

    public DateTime? UsedAtUtc { get; private set; }

    public bool IsUsed => UsedAtUtc.HasValue;

    public EmailVerificationToken(Guid id, Guid userId, string value, DateTime createdAtUtc, DateTime expiresAtUtc)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An email verification token id must be provided.", nameof(id));

        if (userId == Guid.Empty)
            throw new ArgumentException("A user id must be provided.", nameof(userId));

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("An email verification token value is required.", nameof(value));

        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Email verification token expiry must be after its creation.", nameof(expiresAtUtc));

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
        !IsUsed && !IsExpiredAt(utcNow);

    public void MarkUsed(DateTime utcNow)
    {
        if (IsUsed)
            throw new InvalidOperationException("The email verification token has already been used.");

        UsedAtUtc = utcNow;
    }
}
