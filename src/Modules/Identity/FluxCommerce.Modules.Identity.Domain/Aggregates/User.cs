using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Identity.Domain.Entities;
using FluxCommerce.Modules.Identity.Domain.Enums;
using FluxCommerce.Modules.Identity.Domain.Errors;
using FluxCommerce.Modules.Identity.Domain.Events;
using FluxCommerce.Modules.Identity.Domain.Results;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Domain.Aggregates;

public sealed class User : AggregateRoot<Guid>
{
    public const int FirstNameMaxLength = 100;

    public const int LastNameMaxLength = 100;

    public const int PhoneNumberMaxLength = 30;

    public const int AvatarUrlMaxLength = 2048;

    public const int PreferredLanguageMaxLength = 10;

    public const int TimeZoneMaxLength = 64;

    public const string DefaultPreferredLanguage = "en";

    public const string DefaultTimeZone = "UTC";

    private readonly List<RefreshToken> _refreshTokens = [];

    private readonly List<string> _roles = [];

    private readonly List<string> _permissions = [];

    public Email Email { get; }

    public string PasswordHash { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string DisplayName => $"{FirstName} {LastName}";

    public string? PhoneNumber { get; private set; }

    public bool IsPhoneVerified { get; private set; }

    public string? AvatarUrl { get; private set; }

    public string PreferredLanguage { get; private set; }

    public string TimeZone { get; private set; }

    public UserStatus Status { get; private set; }

    public IReadOnlyCollection<string> Roles => _roles.AsReadOnly();

    public IReadOnlyCollection<string> Permissions => _permissions.AsReadOnly();

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
        string firstName,
        string lastName,
        DateTime registeredAtUtc,
        IEnumerable<string> roles,
        LockoutState lockout)
        : base(id)
    {
        Email = email;
        PasswordHash = passwordHash;
        FirstName = ValidateName(firstName, nameof(firstName), FirstNameMaxLength);
        LastName = ValidateName(lastName, nameof(lastName), LastNameMaxLength);
        PhoneNumber = null;
        IsPhoneVerified = false;
        AvatarUrl = null;
        PreferredLanguage = DefaultPreferredLanguage;
        TimeZone = DefaultTimeZone;
        Status = UserStatus.Pending;
        RegisteredAtUtc = registeredAtUtc;
        Lockout = lockout;
        _roles.AddRange(roles);
    }

    private static string ValidateName(string? value, string paramName, int maxLength)
    {
        var name = value?.Trim();

        if (string.IsNullOrEmpty(name))
            throw new ArgumentException($"A {paramName} is required.", paramName);

        if (name.Length > maxLength)
            throw new ArgumentException($"{paramName} must not exceed {maxLength} characters.", paramName);

        return name;
    }

    #pragma warning disable CS8618
    private User() { }
    #pragma warning restore CS8618

    public static User Register(
        Guid id,
        Email email,
        string passwordHash,
        string firstName,
        string lastName,
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

        var user = new User(id, email, passwordHash, firstName, lastName, utcNow, roles, LockoutState.Create());

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
        Status = UserStatus.Active;

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

    /// <summary>
    /// Grants an action-level permission (ADR-016). Unknown permissions are rejected; a repeated
    /// grant fails explicitly so the API can answer 409 instead of silently succeeding.
    /// </summary>
    public DomainResult GrantPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("A permission name is required.", nameof(permission));

        var normalized = permission.Trim();

        if (!PermissionCatalog.IsDefined(normalized))
            throw new ArgumentException($"Unknown permission '{normalized}'.", nameof(permission));

        if (_permissions.Contains(normalized, StringComparer.Ordinal))
            return DomainResult.Failure(UserErrors.PermissionAlreadyGranted);

        _permissions.Add(normalized);

        return DomainResult.Success();
    }

    /// <summary>
    /// Revokes an action-level permission. Absent permissions are a benign no-op so revocation
    /// stays idempotent at the API boundary.
    /// </summary>
    public DomainResult RevokePermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("A permission name is required.", nameof(permission));

        _permissions.RemoveAll(granted => string.Equals(granted, permission.Trim(), StringComparison.Ordinal));

        return DomainResult.Success();
    }

    /// <summary>
    /// Updates the editable profile fields. Each argument is optional: <c>null</c> leaves the
    /// current value unchanged, while an empty value clears an optional field. Required names
    /// must remain non-empty when supplied. Validation of lengths and shapes is the caller's
    /// (application layer's) responsibility; this method enforces the domain invariants.
    /// </summary>
    public void UpdateProfile(
        string? firstName,
        string? lastName,
        string? phoneNumber,
        string? avatarUrl,
        string? preferredLanguage,
        string? timeZone)
    {
        if (firstName is not null)
            FirstName = ValidateName(firstName, nameof(firstName), FirstNameMaxLength);

        if (lastName is not null)
            LastName = ValidateName(lastName, nameof(lastName), LastNameMaxLength);

        if (phoneNumber is not null)
        {
            var phone = phoneNumber.Trim();
            if (phone.Length > PhoneNumberMaxLength)
                throw new ArgumentException($"A phone number must not exceed {PhoneNumberMaxLength} characters.", nameof(phoneNumber));

            PhoneNumber = string.IsNullOrEmpty(phone) ? null : phone;
        }

        if (avatarUrl is not null)
        {
            var avatar = avatarUrl.Trim();
            if (avatar.Length > AvatarUrlMaxLength)
                throw new ArgumentException($"An avatar URL must not exceed {AvatarUrlMaxLength} characters.", nameof(avatarUrl));

            AvatarUrl = string.IsNullOrEmpty(avatar) ? null : avatar;
        }

        if (preferredLanguage is not null)
        {
            var language = preferredLanguage.Trim();
            if (string.IsNullOrEmpty(language))
                throw new ArgumentException("A preferred language is required.", nameof(preferredLanguage));

            if (language.Length > PreferredLanguageMaxLength)
                throw new ArgumentException($"A preferred language must not exceed {PreferredLanguageMaxLength} characters.", nameof(preferredLanguage));

            PreferredLanguage = language;
        }

        if (timeZone is not null)
        {
            var zone = timeZone.Trim();
            if (string.IsNullOrEmpty(zone))
                throw new ArgumentException("A time zone is required.", nameof(timeZone));

            if (zone.Length > TimeZoneMaxLength)
                throw new ArgumentException($"A time zone must not exceed {TimeZoneMaxLength} characters.", nameof(timeZone));

            TimeZone = zone;
        }
    }

    public void Unlock() =>
        Lockout = Lockout.Reset();
}
