using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Identity.Domain.Entities;
using FluxCommerce.Modules.Identity.Domain.Errors;
using FluxCommerce.Modules.Identity.Domain.Events;
using FluxCommerce.Modules.Identity.Domain.Results;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Domain.Aggregates;

public sealed class User : AggregateRoot<Guid>
{
    private readonly List<RefreshToken> _refreshTokens = [];

    private readonly List<string> _roles = [];

    public Email Email { get; }

    public string PasswordHash { get; private set; }

    public IReadOnlyCollection<string> Roles => _roles.AsReadOnly();

    public bool IsEmailVerified { get; private set; }

    public DateTime? EmailVerifiedAtUtc { get; private set; }

    public LockoutState Lockout { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    public DateTime RegisteredAtUtc { get; }

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    public EmailVerificationToken? VerificationToken { get; private set; }

    public PasswordResetToken? ResetToken { get; private set; }

    private User(
        Guid id,
        Email email,
        string passwordHash,
        DateTime registeredAtUtc,
        IEnumerable<string> roles,
        LockoutState lockout)
        : base(id)
    {
        Email = email;
        PasswordHash = passwordHash;
        RegisteredAtUtc = registeredAtUtc;
        Lockout = lockout;
        _roles.AddRange(roles);
    }

    #pragma warning disable CS8618
    private User() { }
    #pragma warning restore CS8618

    public static User Register(
        Guid id,
        Email email,
        string passwordHash,
        DateTime utcNow,
        IReadOnlyCollection<string> roles,
        string? verificationToken = null,
        DateTime? verificationTokenExpiresAtUtc = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A user id must be provided.", nameof(id));

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));

        if (roles is null || roles.Count == 0)
            throw new ArgumentException("At least one role is required.", nameof(roles));

        if (roles.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Role names must not be empty.", nameof(roles));

        if (verificationToken is null && verificationTokenExpiresAtUtc is not null)
            throw new ArgumentException("A verification token is required when its expiry is provided.", nameof(verificationToken));

        if (verificationToken is not null && verificationTokenExpiresAtUtc is null)
            throw new ArgumentException("A verification token expiry is required when a token is provided.", nameof(verificationTokenExpiresAtUtc));

        var user = new User(id, email, passwordHash, utcNow, roles, LockoutState.Create());

        if (verificationToken is not null)
        {
            user.VerificationToken = new EmailVerificationToken(
                Guid.NewGuid(),
                id,
                verificationToken,
                utcNow,
                verificationTokenExpiresAtUtc!.Value);
        }

        user.AddDomainEvent(new UserRegistered(id, email.Value));

        return user;
    }

    public bool IsLockedAt(DateTime utcNow) =>
        Lockout.IsLockedAt(utcNow);

    public DomainResult Authenticate(bool credentialsMatch, DateTime utcNow)
    {
        if (Lockout.IsLockedAt(utcNow))
            return DomainResult.Failure(UserErrors.AccountLocked);

        if (!credentialsMatch)
        {
            Lockout = Lockout.RecordFailedAttempt(utcNow);
            return DomainResult.Failure(UserErrors.InvalidCredentials);
        }

        if (!IsEmailVerified)
            return DomainResult.Failure(UserErrors.EmailNotVerified);

        Lockout = Lockout.Reset();
        LastLoginAtUtc = utcNow;

        return DomainResult.Success();
    }

    public DomainResult VerifyEmail(string token, DateTime utcNow)
    {
        var verificationToken = VerificationToken;

        if (verificationToken is null || !verificationToken.Matches(token))
            return DomainResult.Failure(UserErrors.InvalidToken);

        if (verificationToken.IsExpiredAt(utcNow) || verificationToken.IsUsed)
            return DomainResult.Failure(UserErrors.InvalidToken);

        verificationToken.MarkUsed(utcNow);
        IsEmailVerified = true;
        EmailVerifiedAtUtc = utcNow;

        AddDomainEvent(new EmailVerified(Id));

        return DomainResult.Success();
    }

    public DomainResult ResendVerification(string token, DateTime utcNow, DateTime expiresAtUtc)
    {
        if (IsEmailVerified)
            return DomainResult.Success();

        VerificationToken = new EmailVerificationToken(
            Guid.NewGuid(),
            Id,
            token,
            utcNow,
            expiresAtUtc);

        return DomainResult.Success();
    }

    public DomainResult RequestPasswordReset(string token, DateTime utcNow, DateTime expiresAtUtc)
    {
        ResetToken = new PasswordResetToken(
            Guid.NewGuid(),
            Id,
            token,
            utcNow,
            expiresAtUtc);

        AddDomainEvent(new PasswordResetRequested(Id));

        return DomainResult.Success();
    }

    public DomainResult ResetPassword(string token, string newPasswordHash, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("A new password hash is required.", nameof(newPasswordHash));

        var resetToken = ResetToken;

        if (resetToken is null || !resetToken.Matches(token))
            return DomainResult.Failure(UserErrors.InvalidToken);

        if (resetToken.IsExpiredAt(utcNow) || resetToken.IsUsed)
            return DomainResult.Failure(UserErrors.InvalidToken);

        resetToken.MarkUsed(utcNow);
        PasswordHash = newPasswordHash;
        Lockout = Lockout.Reset();

        AddDomainEvent(new PasswordChanged(Id));

        return DomainResult.Success();
    }

    public DomainResult ChangePassword(bool currentPasswordIsCorrect, string newPasswordHash, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("A new password hash is required.", nameof(newPasswordHash));

        if (!currentPasswordIsCorrect)
            return DomainResult.Failure(UserErrors.InvalidCredentials);

        PasswordHash = newPasswordHash;

        AddDomainEvent(new PasswordChanged(Id));

        return DomainResult.Success();
    }

    public RefreshToken IssueRefreshToken(string tokenValue, DateTime utcNow, DateTime expiresAtUtc)
    {
        var token = new RefreshToken(Guid.NewGuid(), Id, tokenValue, utcNow, expiresAtUtc);
        _refreshTokens.Add(token);

        return token;
    }

    public DomainResult<RefreshToken> RotateRefreshToken(
        string currentTokenValue,
        string newTokenValue,
        DateTime utcNow,
        DateTime newTokenExpiresAtUtc)
    {
        var token = _refreshTokens.FirstOrDefault(t => t.Matches(currentTokenValue) && !t.IsRevoked);

        if (token is null || token.IsExpiredAt(utcNow))
            return DomainResult<RefreshToken>.Failure(UserErrors.InvalidToken);

        var replacement = new RefreshToken(Guid.NewGuid(), Id, newTokenValue, utcNow, newTokenExpiresAtUtc);
        _refreshTokens.Add(replacement);

        token.Revoke(utcNow, newTokenValue);

        AddDomainEvent(new RefreshTokenRevoked(Id, token.Id));

        return DomainResult<RefreshToken>.Success(replacement);
    }

    public DomainResult Logout(string tokenValue, DateTime utcNow)
    {
        var token = _refreshTokens.FirstOrDefault(t => t.Matches(tokenValue));

        if (token is null)
            return DomainResult.Failure(UserErrors.InvalidToken);

        if (token.IsRevoked)
            return DomainResult.Success();

        token.Revoke(utcNow, null);

        AddDomainEvent(new RefreshTokenRevoked(Id, token.Id));

        return DomainResult.Success();
    }

    public DomainResult AssignRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("A role name is required.", nameof(role));

        if (_roles.Contains(role))
            return DomainResult.Failure(UserErrors.RoleAlreadyAssigned);

        _roles.Add(role);

        return DomainResult.Success();
    }

    public void Unlock() =>
        Lockout = Lockout.Reset();
}
