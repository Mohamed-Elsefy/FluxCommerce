using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.Login;

public sealed class LoginCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(LoginCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        EmailRules.Validate(command.Email, errors, localizer);

        if (string.IsNullOrWhiteSpace(command.Password))
            ValidationErrors.Add(errors, "Password", localizer["Validation.Password.Required"]);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
