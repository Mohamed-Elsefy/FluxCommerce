using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;

namespace FluxCommerce.Modules.Identity.Application.Commands.UnlockUser;

public sealed class UnlockUserCommandValidator
{
    private readonly ILocalizer _localizer;

    public UnlockUserCommandValidator(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public ValidationResult Validate(UnlockUserCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (command.UserId == Guid.Empty)
        {
            errors["UserId"] = [ _localizer["Validation.Identity.UserIdRequired"] ];
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
