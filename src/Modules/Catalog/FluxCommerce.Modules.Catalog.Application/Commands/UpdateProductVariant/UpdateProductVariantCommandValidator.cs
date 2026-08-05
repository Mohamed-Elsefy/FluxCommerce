using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductVariant;

public sealed class UpdateProductVariantCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(UpdateProductVariantCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        VariantRules.ValidateId(command.Id, "Id", localizer["Validation.Fields.VariantId"], errors, localizer);
        VariantRules.ValidateSku(command.Sku, errors, localizer);
        VariantRules.ValidateName(command.Name, "Name", localizer["Validation.Fields.VariantName"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
