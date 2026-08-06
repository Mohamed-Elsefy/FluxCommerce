using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.AddProductImage;

public sealed class AddProductImageCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(AddProductImageCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        ImageRules.ValidateUrl(command.ImageUrl, "ImageUrl", localizer["Validation.Fields.ImageUrl"], errors, localizer);
        ImageRules.ValidateAltText(command.AltText, "AltText", localizer["Validation.Fields.AltText"], errors, localizer);
        ImageRules.ValidateDisplayOrder(command.DisplayOrder, "DisplayOrder", localizer["Validation.Fields.DisplayOrder"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
