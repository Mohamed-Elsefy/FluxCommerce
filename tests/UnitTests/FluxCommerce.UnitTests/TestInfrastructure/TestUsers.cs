using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public static class TestUsers
{
    public const string Password = "Str0ng!Pass1";

    public static User CreateVerified(
        Guid? id = null,
        string email = "user@example.com",
        string passwordHash = "hash",
        DateTime? utcNow = null,
        IReadOnlyCollection<string>? roles = null,
        string verificationToken = "verify-token",
        DateTime? verificationTokenExpiresAtUtc = null)
    {
        var now = utcNow ?? new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var user = CreateUnverified(
            id, email, passwordHash, now, roles, verificationToken, verificationTokenExpiresAtUtc ?? now.AddHours(24));

        user.VerifyEmail(verificationToken, now);

        return user;
    }

    public static User CreateUnverified(
        Guid? id = null,
        string email = "user@example.com",
        string passwordHash = "hash",
        DateTime? utcNow = null,
        IReadOnlyCollection<string>? roles = null,
        string? verificationToken = "verify-token",
        DateTime? verificationTokenExpiresAtUtc = null)
    {
        var now = utcNow ?? new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        return User.Register(
            id ?? Guid.NewGuid(),
            new Email(email),
            passwordHash,
            now,
            roles ?? [Roles.Customer],
            verificationToken,
            verificationTokenExpiresAtUtc ?? now.AddHours(24));
    }
}
