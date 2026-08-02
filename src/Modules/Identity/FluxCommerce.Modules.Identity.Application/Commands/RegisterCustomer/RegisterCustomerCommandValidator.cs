using FluxCommerce.Modules.Identity.Application.Validation;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;

public sealed class RegisterCustomerCommandValidator
{
    public ValidationResult Validate(RegisterCustomerCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        EmailRules.Validate(command.Email, errors);
        PasswordPolicy.Validate(command.Password, errors);
        NameRules.Validate(command.FirstName, "FirstName", "First name", User.FirstNameMaxLength, errors);
        NameRules.Validate(command.LastName, "LastName", "Last name", User.LastNameMaxLength, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
