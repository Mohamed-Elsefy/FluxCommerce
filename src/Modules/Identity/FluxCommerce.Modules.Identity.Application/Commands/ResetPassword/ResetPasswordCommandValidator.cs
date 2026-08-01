using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.ResetPassword;

public sealed class ResetPasswordCommandValidator
{
    public ValidationResult Validate(ResetPasswordCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        TokenRules.Validate(command.Token, "Token", errors);
        PasswordPolicy.Validate(command.NewPassword, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
