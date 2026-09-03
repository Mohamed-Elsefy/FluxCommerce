using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.Refresh;

public sealed class RefreshCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(RefreshCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        TokenRules.Validate(command.RefreshToken, "RefreshToken", localizer["Validation.Fields.RefreshToken"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
