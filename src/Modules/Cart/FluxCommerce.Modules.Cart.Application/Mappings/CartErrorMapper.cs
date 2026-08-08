using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Domain.Errors;

namespace FluxCommerce.Modules.Cart.Application.Mappings;

/// <summary>
/// Maps Cart Domain errors to the shared Application Result/Error model while keeping
/// the stable machine-readable error code and English fallback description.
/// </summary>
public static class CartErrorMapper
{
    public static Error ToApplicationError(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Code switch
        {
            "Cart.InvalidQuantity" => Error.Validation(error.Code, error.Description),
            "Cart.QuantityLimitExceeded" => Error.Validation(error.Code, error.Description),
            "Cart.CartItemNotFound" => Error.NotFound(error.Code, error.Description),
            "Cart.CartNotFound" => Error.NotFound(error.Code, error.Description),
            "Cart.CartAccessDenied" => Error.NotFound(error.Code, error.Description),
            "Cart.CartConcurrencyConflict" => Error.Conflict(error.Code, error.Description),
            "Cart.InvalidCartItem" => Error.Validation(error.Code, error.Description),
            "Cart.InvalidCartId" => Error.Validation(error.Code, error.Description),
            _ => Error.Validation(error.Code, error.Description),
        };
    }

    public static Error Validation(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return Error.Validation(error.Code, error.Description);
    }
}
