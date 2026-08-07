using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Catalog.Application.Validation;

namespace FluxCommerce.Modules.Catalog.Application.Queries.ListProducts;

public sealed class ListProductsQueryValidator(ILocalizer localizer)
{
    public ValidationResult Validate(ListProductsQuery query)
    {
        var errors = new Dictionary<string, string[]>();

        PaginationRules.ValidatePageNumber(query.PageNumber, "PageNumber", localizer["Validation.Fields.PageNumber"], errors, localizer);
        PaginationRules.ValidatePageSize(query.PageSize, "PageSize", localizer["Validation.Fields.PageSize"], errors, localizer);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
