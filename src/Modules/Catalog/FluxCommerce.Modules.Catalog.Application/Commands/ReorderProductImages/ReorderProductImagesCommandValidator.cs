using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ReorderProductImages;

public sealed class ReorderProductImagesCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(ReorderProductImagesCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        ImageRules.ValidateOrderedImageIds(command.OrderedImageIds, "OrderedImageIds", localizer["Validation.Fields.Images"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
