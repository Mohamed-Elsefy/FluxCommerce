using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.ForgotPassword;

public sealed class ForgotPasswordCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(ForgotPasswordCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        EmailRules.Validate(command.Email, errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
