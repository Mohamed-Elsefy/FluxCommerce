using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.Refresh;

public sealed class RefreshCommandValidator
{
    public ValidationResult Validate(RefreshCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        RefreshTokenRules.Validate(command.RefreshToken, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
