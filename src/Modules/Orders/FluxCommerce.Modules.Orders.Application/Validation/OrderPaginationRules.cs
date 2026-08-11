using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.Modules.Orders.Application.Validation;

/// <summary>
/// Shared pagination rules applied by the customer-facing Orders list query.
/// </summary>
public static class OrderPaginationRules
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
                "Order.ValidationFailed",
                $"The page number must be at least {MinPageNumber}.");

        if (pageSize is < MinPageSize or > MaxPageSize)
            return Error.Validation(
                "Order.ValidationFailed",
                $"The page size must be between {MinPageSize} and {MaxPageSize}.");

        return null;
    }
}
