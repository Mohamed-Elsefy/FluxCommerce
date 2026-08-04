using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.RestoreCategory;

public sealed class RestoreCategoryCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(RestoreCategoryCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        CategoryRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.CategoryId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
