using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.Login;

public sealed class LoginCommandValidator
{
    public ValidationResult Validate(LoginCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        EmailRules.Validate(command.Email, errors);

        if (string.IsNullOrWhiteSpace(command.Password))
            ValidationErrors.Add(errors, "Password", "Password is required.");

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
