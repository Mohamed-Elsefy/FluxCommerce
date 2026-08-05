using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductAttributeValue;

public sealed class UpdateProductAttributeValueCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(UpdateProductAttributeValueCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        AttributeRules.ValidateId(command.AttributeDefinitionId, "AttributeDefinitionId", localizer["Validation.Fields.AttributeId"], errors, localizer);
        AttributeRules.ValidateValue(command.Value, "Value", localizer["Validation.Fields.AttributeValue"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
