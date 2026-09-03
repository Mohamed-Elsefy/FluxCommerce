using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.RestoreAttributeDefinition;

public sealed class RestoreAttributeDefinitionCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(RestoreAttributeDefinitionCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        AttributeRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.AttributeId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
