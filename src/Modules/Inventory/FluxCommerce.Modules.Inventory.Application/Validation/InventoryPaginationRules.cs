using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.Modules.Inventory.Application.Validation;

/// <summary>
/// Shared pagination rules applied by the administrative Inventory list queries.
/// </summary>
public static class InventoryPaginationRules
{
    public const int DefaultPageNumber = 1;

    public const int DefaultPageSize = 20;

    public const int MinPageNumber = 1;

    public const int MinPageSize = 1;

    public const int MaxPageSize = 100;

    public static Error? Validate(int pageNumber, int pageSize)
    {
        if (pageNumber < MinPageNumber)
            return Error.Validation(
                "Inventory.ValidationFailed",
                $"The page number must be at least {MinPageNumber}.");

        if (pageSize is < MinPageSize or > MaxPageSize)
            return Error.Validation(
                "Inventory.ValidationFailed",
                $"The page size must be between {MinPageSize} and {MaxPageSize}.");

        return null;
    }
}
