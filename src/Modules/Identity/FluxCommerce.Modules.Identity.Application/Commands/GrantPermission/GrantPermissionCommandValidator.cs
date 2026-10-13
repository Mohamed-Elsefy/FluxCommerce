using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Commands.GrantPermission;

public sealed class GrantPermissionCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(GrantPermissionCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (command.UserId == Guid.Empty)
            ValidationErrors.Add(errors, "UserId", localizer.Get("Validation.Field.Required", localizer["Validation.Fields.UserId"]));

        ValidatePermission(command.Permission, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    private void ValidatePermission(string? permission, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            ValidationErrors.Add(errors, "Permission", localizer.Get("Validation.Field.Required", localizer["Validation.Fields.Permission"]));
            return;
        }

        if (!PermissionCatalog.IsDefined(permission))
            ValidationErrors.Add(errors, "Permission", localizer.Get("Validation.Permission.Invalid", permission.Trim()));
    }
}
