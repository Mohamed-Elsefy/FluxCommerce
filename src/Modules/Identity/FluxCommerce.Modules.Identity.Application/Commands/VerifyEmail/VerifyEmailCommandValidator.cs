using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailCommandValidator
{
    public ValidationResult Validate(VerifyEmailCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        TokenRules.Validate(command.Token, "Token", errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
