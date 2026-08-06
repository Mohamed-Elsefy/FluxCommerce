using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeactivateCategory;

public sealed class DeactivateCategoryCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(DeactivateCategoryCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        CategoryRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.CategoryId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
