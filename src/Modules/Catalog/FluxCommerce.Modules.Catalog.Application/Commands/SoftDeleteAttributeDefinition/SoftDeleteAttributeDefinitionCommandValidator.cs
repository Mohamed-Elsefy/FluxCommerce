using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteAttributeDefinition;

public sealed class SoftDeleteAttributeDefinitionCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(SoftDeleteAttributeDefinitionCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        AttributeRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.AttributeId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
