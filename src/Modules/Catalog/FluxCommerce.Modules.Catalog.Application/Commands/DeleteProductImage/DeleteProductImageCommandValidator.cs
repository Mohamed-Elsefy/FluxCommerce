using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductImage;

public sealed class DeleteProductImageCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(DeleteProductImageCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        ImageRules.ValidateId(command.ImageId, "ImageId", localizer["Validation.Fields.ImageId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
