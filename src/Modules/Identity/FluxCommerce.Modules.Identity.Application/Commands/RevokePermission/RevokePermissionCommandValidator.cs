using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Commands.RevokePermission;

public sealed class RevokePermissionCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(RevokePermissionCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (command.UserId == Guid.Empty)
            ValidationErrors.Add(errors, "UserId", localizer.Get("Validation.Field.Required", localizer["Validation.Fields.UserId"]));

        if (string.IsNullOrWhiteSpace(command.Permission))
            ValidationErrors.Add(errors, "Permission", localizer.Get("Validation.Field.Required", localizer["Validation.Fields.Permission"]));
        else if (!PermissionCatalog.IsDefined(command.Permission))
            ValidationErrors.Add(errors, "Permission", localizer.Get("Validation.Permission.Invalid", command.Permission.Trim()));

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
