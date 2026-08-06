using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateAttributeDefinition;

public sealed class UpdateAttributeDefinitionCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(UpdateAttributeDefinitionCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        AttributeRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.AttributeId"], errors, localizer);
        AttributeRules.ValidateName(command.Name, "Name", localizer["Validation.Fields.AttributeName"], errors, localizer);
        AttributeRules.ValidateSlug(command.Slug, errors, localizer);
        AttributeRules.ValidateDescription(command.Description, "Description", localizer["Validation.Fields.Description"], errors, localizer);
        AttributeRules.ValidateDataType(command.DataType, "DataType", localizer["Validation.Fields.DataType"], errors, localizer);
        AttributeRules.ValidateSortOrder(command.SortOrder, "SortOrder", localizer["Validation.Fields.SortOrder"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
