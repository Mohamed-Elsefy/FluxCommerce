using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProduct;

public sealed class UpdateProductCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(UpdateProductCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.ProductId"], errors, localizer);
        ProductRules.ValidateName(command.Name, "Name", localizer["Validation.Fields.ProductName"], errors, localizer);
        ProductRules.ValidateSlug(command.Slug, errors, localizer);
        ProductRules.ValidateDescription(command.Description, "Description", localizer["Validation.Fields.Description"], errors, localizer);
        ProductRules.ValidateId(command.CategoryId, "CategoryId", localizer["Validation.Fields.CategoryId"], errors, localizer);
        ProductRules.ValidateVisibility(command.Visibility, "Visibility", localizer["Validation.Fields.Visibility"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
