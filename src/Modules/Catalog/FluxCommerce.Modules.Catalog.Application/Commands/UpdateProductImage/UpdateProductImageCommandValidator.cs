using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductImage;

public sealed class UpdateProductImageCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(UpdateProductImageCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        ImageRules.ValidateId(command.ImageId, "ImageId", localizer["Validation.Fields.ImageId"], errors, localizer);
        ImageRules.ValidateUrl(command.ImageUrl, "ImageUrl", localizer["Validation.Fields.ImageUrl"], errors, localizer);
        ImageRules.ValidateAltText(command.AltText, "AltText", localizer["Validation.Fields.AltText"], errors, localizer);
        ImageRules.ValidateDisplayOrder(command.DisplayOrder, "DisplayOrder", localizer["Validation.Fields.DisplayOrder"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
