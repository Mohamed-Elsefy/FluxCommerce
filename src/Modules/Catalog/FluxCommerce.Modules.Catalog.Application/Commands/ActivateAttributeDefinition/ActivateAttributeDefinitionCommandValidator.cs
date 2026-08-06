using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.ActivateAttributeDefinition;

public sealed class ActivateAttributeDefinitionCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(ActivateAttributeDefinitionCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        AttributeRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.AttributeId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
