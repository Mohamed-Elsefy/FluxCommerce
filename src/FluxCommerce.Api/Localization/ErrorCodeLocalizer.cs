using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Localization;

namespace FluxCommerce.Api.Localization;

public sealed class ErrorCodeLocalizer(ILocalizer localizer) : IErrorCodeLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> CodeToKey = new Dictionary<string, string>
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

    public string Localize(string code, string fallback)
    {
        if (!CodeToKey.TryGetValue(code, out var key))
            return fallback;

        var value = localizer.Get(key);

        return value == key ? fallback : value;
    }
}