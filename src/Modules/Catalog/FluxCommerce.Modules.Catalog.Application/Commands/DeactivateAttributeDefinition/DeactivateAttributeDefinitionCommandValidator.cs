using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeactivateAttributeDefinition;

public sealed class DeactivateAttributeDefinitionCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(DeactivateAttributeDefinitionCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        AttributeRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.AttributeId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
