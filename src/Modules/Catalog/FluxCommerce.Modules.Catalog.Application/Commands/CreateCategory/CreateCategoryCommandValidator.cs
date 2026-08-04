using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateCategory;

public sealed class CreateCategoryCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(CreateCategoryCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        CategoryRules.ValidateName(command.Name, "Name", localizer["Validation.Fields.CategoryName"], errors, localizer);
        CategoryRules.ValidateSlug(command.Slug, errors, localizer);
        CategoryRules.ValidateDescription(command.Description, "Description", localizer["Validation.Fields.Description"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
