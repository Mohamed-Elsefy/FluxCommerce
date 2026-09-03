using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.RemoveProductAttributeValue;

public sealed class RemoveProductAttributeValueCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(RemoveProductAttributeValueCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        ProductRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        AttributeRules.ValidateId(command.AttributeDefinitionId, "AttributeDefinitionId", localizer["Validation.Fields.AttributeId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
