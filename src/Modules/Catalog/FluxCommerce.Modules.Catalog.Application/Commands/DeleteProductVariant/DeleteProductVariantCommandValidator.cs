using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductVariant;

public sealed class DeleteProductVariantCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(DeleteProductVariantCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        VariantRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.VariantId"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
