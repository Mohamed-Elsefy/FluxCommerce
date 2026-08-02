namespace FluxCommerce.Modules.Identity.Domain.Errors;

public static class UserErrors
{
    public static readonly DomainError DuplicateEmail = DomainError.Create(
        "Identity.DuplicateEmail",
        "An account with this email address already exists.");

    public static readonly DomainError InvalidCredentials = DomainError.Create(
        "Identity.InvalidCredentials",
        "The email or password is incorrect.");

    public static readonly DomainError EmailNotVerified = DomainError.Create(
        "Identity.EmailNotVerified",
        "The email address has not been verified.");

    public static readonly DomainError AccountLocked = DomainError.Create(
        "Identity.AccountLocked",
        "The account is locked due to repeated failed sign-in attempts.");

    public static readonly DomainError InvalidToken = DomainError.Create(
        "Identity.InvalidToken",
        "The token is invalid, expired, or has already been used.");

    public static readonly DomainError RoleAlreadyAssigned = DomainError.Create(
        "Identity.RoleAlreadyAssigned",
        "The role is already assigned to this user.");
}
