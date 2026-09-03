using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariants;

public sealed class CreateProductVariantsCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(CreateProductVariantsCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        VariantRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);

        for (var i = 0; i < command.Variants.Count; i++)
        {
            var item = command.Variants[i];
            VariantRules.ValidateSku(item.Sku, $"Variants[{i}].Sku", errors, localizer);
            VariantRules.ValidateName(item.Name, $"Variants[{i}].Name", localizer["Validation.Fields.VariantName"], errors, localizer);
            VariantRules.ValidateUnitPrice(item.UnitPriceAmount, item.Currency, $"Variants[{i}].UnitPriceAmount", $"Variants[{i}].Currency", errors, localizer);
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
