using FluxCommerce.BuildingBlocks.Localization.Abstractions;

namespace FluxCommerce.Modules.Catalog.Application.Validation;

public static class PaginationRules
{
    public const int MinPageNumber = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;

    public static void ValidatePageNumber(
        int? pageNumber,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (pageNumber is not null && pageNumber.Value < MinPageNumber)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Invalid", displayName));
    }

    public static void ValidatePageSize(
        int? pageSize,
        string propertyName,
        string displayName,
        Dictionary<string, string[]> errors,
        ILocalizer localizer)
    {
        if (pageSize is not null && (pageSize.Value < MinPageSize || pageSize.Value > MaxPageSize))
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Range", displayName, MinPageSize, MaxPageSize));
    }
}
