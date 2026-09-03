using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariant;

public sealed class CreateProductVariantCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(CreateProductVariantCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        VariantRules.ValidateId(command.ProductId, "ProductId", localizer["Validation.Fields.ProductId"], errors, localizer);
        VariantRules.ValidateSku(command.Sku, errors, localizer);
        VariantRules.ValidateName(command.Name, "Name", localizer["Validation.Fields.VariantName"], errors, localizer);
        VariantRules.ValidateUnitPrice(command.UnitPriceAmount, command.Currency, "UnitPriceAmount", "Currency", errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
