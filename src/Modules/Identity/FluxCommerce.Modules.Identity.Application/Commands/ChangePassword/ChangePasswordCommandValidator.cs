using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator
{
    public ValidationResult Validate(ChangePasswordCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.CurrentPassword))
            ValidationErrors.Add(errors, "CurrentPassword", "CurrentPassword is required.");

        PasswordPolicy.Validate(command.NewPassword, errors);

        if (command.NewPassword != command.ConfirmPassword)
            ValidationErrors.Add(errors, "ConfirmPassword", "Passwords do not match.");

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
