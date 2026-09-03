using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.VerifyEmail;

public sealed class VerifyEmailCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(VerifyEmailCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        TokenRules.Validate(command.Token, "Token", localizer["Validation.Fields.Token"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
