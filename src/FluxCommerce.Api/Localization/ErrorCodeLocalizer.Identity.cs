namespace FluxCommerce.Api.Localization;

public sealed partial class ErrorCodeLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> IdentityEntries =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Identity.ValidationFailed"] = "Errors.RequestValidation",
            ["Identity.InvalidCredentials"] = "Authentication.Login.InvalidCredentials",
            ["Identity.EmailNotVerified"] = "Authentication.Login.EmailNotConfirmed",
            ["Identity.AccountLocked"] = "Authentication.Login.LockedOut",
            ["Identity.InvalidToken"] = "Errors.InvalidToken",
            ["Identity.DuplicateEmail"] = "Authentication.Register.EmailAlreadyExists",
            ["Identity.InvalidEmail"] = "Validation.Email.Invalid",
            ["Identity.Unauthenticated"] = "Authorization.Unauthorized",
            ["Identity.UserNotFound"] = "Errors.NotFound",
            ["Identity.EmailSendFailed"] = "Errors.EmailSendFailed",
            ["Identity.RoleAlreadyAssigned"] = "Errors.RoleAlreadyAssigned",
        };
}
