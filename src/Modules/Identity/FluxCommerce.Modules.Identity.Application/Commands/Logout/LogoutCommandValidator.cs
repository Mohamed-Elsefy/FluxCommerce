using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.Logout;

public sealed class LogoutCommandValidator
{
    public ValidationResult Validate(LogoutCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        RefreshTokenRules.Validate(command.RefreshToken, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
