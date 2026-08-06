using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductSpecification;

public sealed class CreateProductSpecificationCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(CreateProductSpecificationCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        SpecificationRules.ValidateName(command.Name, "Name", localizer["Validation.Fields.SpecificationName"], errors, localizer);
        SpecificationRules.ValidateValue(command.Value, "Value", localizer["Validation.Fields.SpecificationValue"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
