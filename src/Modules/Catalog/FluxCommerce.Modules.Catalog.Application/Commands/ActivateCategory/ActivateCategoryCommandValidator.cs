using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ActivateCategory;

public sealed class ActivateCategoryCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(ActivateCategoryCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        CategoryRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.CategoryId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
