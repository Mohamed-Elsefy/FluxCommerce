using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Errors;
using FluxCommerce.Modules.Identity.Domain.Events;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class UserAggregateTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static User CreateVerifiedUser(string email = "user@example.com", string passwordHash = "current-hash")
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email(email),
            passwordHash,
            UtcNow,
            [Roles.Customer],
            "verify-token",
            UtcNow.AddHours(24));

        user.VerifyEmail("verify-token", UtcNow);

        return user;
    }

    // ---- Registration ----

    [Fact]
    public void Register_CreatesUserWithRolesAndRaisesEvent()
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email("user@example.com"),
            "password-hash",
            UtcNow,
            [Roles.Customer],
            "verify-token",
            UtcNow.AddHours(24));

        Assert.Equal("user@example.com", user.Email.Value);
        Assert.Equal("password-hash", user.PasswordHash);
        Assert.Contains(Roles.Customer, user.Roles);
        Assert.False(user.IsEmailVerified);
        Assert.Equal(UtcNow, user.RegisteredAtUtc);
        Assert.True(user.GetDomainEvents().OfType<UserRegistered>().Any());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Register_WithInvalidArguments_Throws(string? passwordHash)
    {
        Assert.Throws<ArgumentException>(() =>
            User.Register(Guid.NewGuid(), new Email("user@example.com"), passwordHash!, UtcNow, [Roles.Customer]));

        Assert.Throws<ArgumentException>(() =>
            User.Register(Guid.Empty, new Email("user@example.com"), "hash", UtcNow, [Roles.Customer]));

        Assert.Throws<ArgumentException>(() =>
            User.Register(Guid.NewGuid(), new Email("user@example.com"), "hash", UtcNow, []));
    }

    // ---- Authentication / lockout ----

    [Fact]
    public void Authenticate_WithCorrectCredentials_Succeeds_AndTracksLastLogin()
    {
        var user = CreateVerifiedUser();

        var result = user.Authenticate(true, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(UtcNow, user.LastLoginAtUtc);
        Assert.Equal(0, user.Lockout.FailedAccessAttemptCount);
    }

    [Fact]
    public void Authenticate_WithWrongCredentials_Fails_AndRecordsAttempt()
    {
        var user = CreateVerifiedUser();

        var result = user.Authenticate(false, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidCredentials, result.Error);
        Assert.Equal(1, user.Lockout.FailedAccessAttemptCount);
    }

    [Fact]
    public void Authenticate_WhenLocked_Fails()
    {
        var user = CreateVerifiedUser();

        for (var i = 0; i < 5; i++)
        {
            user.Authenticate(false, UtcNow);
        }

        var result = user.Authenticate(true, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.AccountLocked, result.Error);
    }

    [Fact]
    public void Authenticate_WithUnverifiedEmail_Fails()
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email("user@example.com"),
            "hash",
            UtcNow,
            [Roles.Customer],
            "verify-token",
            UtcNow.AddHours(24));

        var result = user.Authenticate(true, UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.EmailNotVerified, result.Error);
    }

    [Fact]
    public void Authenticate_AfterLockoutWindow_Succeeds_AndResetsLockout()
    {
        var user = CreateVerifiedUser();

        for (var i = 0; i < 5; i++)
        {
            user.Authenticate(false, UtcNow);
        }

        var afterLockout = UtcNow.AddMinutes(16);

        var result = user.Authenticate(true, afterLockout);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, user.Lockout.FailedAccessAttemptCount);
        Assert.Null(user.Lockout.LockoutEndUtc);
    }

    [Fact]
    public void SuccessfulAuthentication_ResetsFailedAttempts()
    {
        var user = CreateVerifiedUser();

        user.Authenticate(false, UtcNow);
        user.Authenticate(false, UtcNow);

        var result = user.Authenticate(true, UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, user.Lockout.FailedAccessAttemptCount);
    }

    // ---- Email verification ----

    [Fact]
    public void VerifyEmail_WithCorrectToken_Succeeds_AndRaisesEvent()
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email("user@example.com"),
            "hash",
            UtcNow,
            [Roles.Customer],
            "verify-token",
            UtcNow.AddHours(24));

        var result = user.VerifyEmail("verify-token", UtcNow);

        Assert.True(result.IsSuccess);
        Assert.True(user.IsEmailVerified);
        Assert.Equal(UtcNow, user.EmailVerifiedAtUtc);
        Assert.True(user.GetDomainEvents().OfType<EmailVerified>().Any());
    }

    [Fact]
    public void VerifyEmail_WithWrongToken_Fails()
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email("user@example.com"),
            "hash",
            UtcNow,
            [Roles.Customer],
            "verify-token",
            UtcNow.AddHours(24));

        var result = user.VerifyEmail("wrong-token", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, result.Error);
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public void VerifyEmail_WithExpiredToken_Fails()
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email("user@example.com"),
            "hash",
            UtcNow,
            [Roles.Customer],
            "verify-token",
            UtcNow.AddHours(24));

        var result = user.VerifyEmail("verify-token", UtcNow.AddHours(25));

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, result.Error);
    }

    [Fact]
    public void VerifyEmail_TokenIsSingleUse()
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email("user@example.com"),
            "hash",
            UtcNow,
            [Roles.Customer],
            "verify-token",
            UtcNow.AddHours(24));

        Assert.True(user.VerifyEmail("verify-token", UtcNow).IsSuccess);

        var replay = user.VerifyEmail("verify-token", UtcNow);

        Assert.True(replay.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, replay.Error);
    }

    [Fact]
    public void ResendVerification_ReplacesToken()
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email("user@example.com"),
            "hash",
            UtcNow,
            [Roles.Customer],
            "old-token",
            UtcNow.AddHours(24));

        var result = user.ResendVerification("new-token", UtcNow, UtcNow.AddHours(24));

        Assert.True(result.IsSuccess);
        Assert.True(user.VerificationToken!.Matches("new-token"));
        Assert.True(user.VerifyEmail("old-token", UtcNow).IsFailure);
    }

    // ---- Password reset ----

    [Fact]
    public void RequestPasswordReset_SetsResetToken_AndRaisesEvent()
    {
        var user = CreateVerifiedUser();

        var result = user.RequestPasswordReset("reset-token", UtcNow, UtcNow.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.True(user.ResetToken!.Matches("reset-token"));
        Assert.True(user.GetDomainEvents().OfType<PasswordResetRequested>().Any());
    }

    [Fact]
    public void ResetPassword_WithValidToken_Succeeds_AndRaisesEvent()
    {
        var user = CreateVerifiedUser();
        user.RequestPasswordReset("reset-token", UtcNow, UtcNow.AddHours(1));

        var result = user.ResetPassword("reset-token", "new-hash", UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", user.PasswordHash);
        Assert.True(user.ResetToken!.IsUsed);
        Assert.True(user.GetDomainEvents().OfType<PasswordChanged>().Any());
    }

    [Fact]
    public void ResetPassword_WithExpiredToken_Fails()
    {
        var user = CreateVerifiedUser();
        user.RequestPasswordReset("reset-token", UtcNow, UtcNow.AddHours(1));

        var result = user.ResetPassword("reset-token", "new-hash", UtcNow.AddHours(2));

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, result.Error);
        Assert.Equal("current-hash", user.PasswordHash);
    }

    [Fact]
    public void ResetPassword_TokenIsSingleUse()
    {
        var user = CreateVerifiedUser();
        user.RequestPasswordReset("reset-token", UtcNow, UtcNow.AddHours(1));

        Assert.True(user.ResetPassword("reset-token", "new-hash", UtcNow).IsSuccess);

        var replay = user.ResetPassword("reset-token", "another-hash", UtcNow);

        Assert.True(replay.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, replay.Error);
        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void ResetPassword_WithWrongToken_Fails()
    {
        var user = CreateVerifiedUser();
        user.RequestPasswordReset("reset-token", UtcNow, UtcNow.AddHours(1));

        var result = user.ResetPassword("wrong-token", "new-hash", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, result.Error);
    }

    [Fact]
    public void ResetPassword_ResetsLockout()
    {
        var user = CreateVerifiedUser();

        for (var i = 0; i < 5; i++)
        {
            user.Authenticate(false, UtcNow);
        }

        user.RequestPasswordReset("reset-token", UtcNow, UtcNow.AddHours(1));

        var result = user.ResetPassword("reset-token", "new-hash", UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, user.Lockout.FailedAccessAttemptCount);
        Assert.Null(user.Lockout.LockoutEndUtc);
    }

    // ---- Change password ----

    [Fact]
    public void ChangePassword_WithCorrectCurrentPassword_Succeeds_AndRaisesEvent()
    {
        var user = CreateVerifiedUser();

        var result = user.ChangePassword(true, "new-hash", UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", user.PasswordHash);
        Assert.True(user.GetDomainEvents().OfType<PasswordChanged>().Any());
    }

    [Fact]
    public void ChangePassword_WithWrongCurrentPassword_Fails()
    {
        var user = CreateVerifiedUser();

        var result = user.ChangePassword(false, "new-hash", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidCredentials, result.Error);
        Assert.Equal("current-hash", user.PasswordHash);
    }

    [Fact]
    public void ChangePassword_WithEmptyNewHash_Throws()
    {
        var user = CreateVerifiedUser();

        Assert.Throws<ArgumentException>(() => user.ChangePassword(true, "", UtcNow));
    }

    // ---- Refresh tokens ----

    [Fact]
    public void IssueRefreshToken_AddsToken()
    {
        var user = CreateVerifiedUser();

        var token = user.IssueRefreshToken("token-1", UtcNow, UtcNow.AddDays(7));

        Assert.True(user.RefreshTokens.Single().Matches("token-1"));
        Assert.Equal(token.UserId, user.Id);
    }

    [Fact]
    public void RotateRefreshToken_RevokesOldToken_AndIssuesReplacement()
    {
        var user = CreateVerifiedUser();
        user.IssueRefreshToken("token-1", UtcNow, UtcNow.AddDays(7));

        var result = user.RotateRefreshToken("token-1", "token-2", UtcNow, UtcNow.AddDays(7));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Matches("token-2"));
        var old = user.RefreshTokens.Single(token => token.Matches("token-1"));
        Assert.True(old.IsRevoked);
        Assert.Equal("token-2", old.ReplacedByTokenValue);
        Assert.True(user.GetDomainEvents().OfType<RefreshTokenRevoked>().Any());
    }

    [Fact]
    public void RotateRefreshToken_ReplayOfUsedToken_Fails()
    {
        var user = CreateVerifiedUser();
        user.IssueRefreshToken("token-1", UtcNow, UtcNow.AddDays(7));
        user.RotateRefreshToken("token-1", "token-2", UtcNow, UtcNow.AddDays(7));

        var replay = user.RotateRefreshToken("token-1", "token-3", UtcNow, UtcNow.AddDays(7));

        Assert.True(replay.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, replay.Error);
    }

    [Fact]
    public void RotateRefreshToken_WithUnknownToken_Fails()
    {
        var user = CreateVerifiedUser();

        var result = user.RotateRefreshToken("unknown", "token-2", UtcNow, UtcNow.AddDays(7));

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, result.Error);
    }

    [Fact]
    public void RotateRefreshToken_WithExpiredToken_Fails()
    {
        var user = CreateVerifiedUser();
        user.IssueRefreshToken("token-1", UtcNow, UtcNow.AddDays(7));

        var result = user.RotateRefreshToken("token-1", "token-2", UtcNow.AddDays(8), UtcNow.AddDays(15));

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, result.Error);
    }

    [Fact]
    public void Logout_RevokesToken_AndRaisesEvent()
    {
        var user = CreateVerifiedUser();
        user.IssueRefreshToken("token-1", UtcNow, UtcNow.AddDays(7));

        var result = user.Logout("token-1", UtcNow);

        Assert.True(result.IsSuccess);
        Assert.True(user.RefreshTokens.Single().IsRevoked);
        Assert.True(user.GetDomainEvents().OfType<RefreshTokenRevoked>().Any());
    }

    [Fact]
    public void Logout_RevokedToken_IsIdempotent()
    {
        var user = CreateVerifiedUser();
        user.IssueRefreshToken("token-1", UtcNow, UtcNow.AddDays(7));
        user.Logout("token-1", UtcNow);

        var result = user.Logout("token-1", UtcNow);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Logout_WithUnknownToken_Fails()
    {
        var user = CreateVerifiedUser();

        var result = user.Logout("unknown", UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidToken, result.Error);
    }

    // ---- Roles / unlock ----

    [Fact]
    public void AssignRole_AddsRole_AndRejectsDuplicates()
    {
        var user = CreateVerifiedUser();

        Assert.True(user.AssignRole(Roles.Admin).IsSuccess);
        Assert.Contains(Roles.Admin, user.Roles);

        var duplicate = user.AssignRole(Roles.Admin);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(UserErrors.RoleAlreadyAssigned, duplicate.Error);
    }

    [Fact]
    public void Unlock_ResetsLockout()
    {
        var user = CreateVerifiedUser();

        for (var i = 0; i < 5; i++)
        {
            user.Authenticate(false, UtcNow);
        }

        Assert.True(user.IsLockedAt(UtcNow));

        user.Unlock();

        Assert.False(user.IsLockedAt(UtcNow));
        Assert.Equal(0, user.Lockout.FailedAccessAttemptCount);
    }
}
