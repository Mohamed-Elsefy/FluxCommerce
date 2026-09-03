using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductSpecification;

public sealed class UpdateProductSpecificationCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(UpdateProductSpecificationCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        SpecificationRules.ValidateId(command.SpecificationId, "SpecificationId", localizer["Validation.Fields.SpecificationId"], errors, localizer);
        SpecificationRules.ValidateName(command.Name, "Name", localizer["Validation.Fields.SpecificationName"], errors, localizer);
        SpecificationRules.ValidateValue(command.Value, "Value", localizer["Validation.Fields.SpecificationValue"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
