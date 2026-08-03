using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;

public sealed class RegisterAdminCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(RegisterAdminCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        EmailRules.Validate(command.Email, errors, localizer);
        PasswordPolicy.Validate(command.Password, errors, localizer);
        NameRules.Validate(command.FirstName, "FirstName", localizer["Validation.Fields.FirstName"], User.FirstNameMaxLength, errors, localizer);
        NameRules.Validate(command.LastName, "LastName", localizer["Validation.Fields.LastName"], User.LastNameMaxLength, errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
