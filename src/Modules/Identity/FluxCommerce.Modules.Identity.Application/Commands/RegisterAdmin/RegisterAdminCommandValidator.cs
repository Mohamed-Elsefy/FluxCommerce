using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;

public sealed class RegisterAdminCommandValidator
{
    public ValidationResult Validate(RegisterAdminCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        EmailRules.Validate(command.Email, errors);
        PasswordPolicy.Validate(command.Password, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
