using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.ResetPassword;

public sealed class ResetPasswordCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(ResetPasswordCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        TokenRules.Validate(command.Token, "Token", localizer["Validation.Fields.Token"], errors, localizer);
        PasswordPolicy.Validate(command.NewPassword, errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
