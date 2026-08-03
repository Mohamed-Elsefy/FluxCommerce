using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(ChangePasswordCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.CurrentPassword))
            ValidationErrors.Add(errors, "CurrentPassword", localizer.Get("Validation.Field.Required", localizer["Validation.Fields.CurrentPassword"]));

        PasswordPolicy.Validate(command.NewPassword, errors, localizer);

        if (command.NewPassword != command.ConfirmPassword)
            ValidationErrors.Add(errors, "ConfirmPassword", localizer["Validation.Password.Mismatch"]);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
