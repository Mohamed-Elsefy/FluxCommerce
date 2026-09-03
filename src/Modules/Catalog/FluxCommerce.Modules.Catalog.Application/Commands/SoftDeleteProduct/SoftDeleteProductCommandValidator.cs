using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteProduct;

public sealed class SoftDeleteProductCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(SoftDeleteProductCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.ProductId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
