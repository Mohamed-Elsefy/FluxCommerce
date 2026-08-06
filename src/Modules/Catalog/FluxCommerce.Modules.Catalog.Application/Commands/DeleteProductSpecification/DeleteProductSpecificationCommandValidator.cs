using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductSpecification;

public sealed class DeleteProductSpecificationCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(DeleteProductSpecificationCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        SpecificationRules.ValidateId(command.SpecificationId, "SpecificationId", localizer["Validation.Fields.SpecificationId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
