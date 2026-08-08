using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Cart.Domain.Errors;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class CartErrorsTests
{
    [Fact]
    public void InvalidQuantity_HasStableCode()
    {
        Assert.Equal("Cart.InvalidQuantity", CartErrors.InvalidQuantity.Code);
        Assert.Equal("A cart item quantity must be a positive integer.", CartErrors.InvalidQuantity.Description);
    }

    [Fact]
    public void CartItemNotFound_HasStableCode()
    {
        Assert.Equal("Cart.CartItemNotFound", CartErrors.CartItemNotFound.Code);
        Assert.Equal("The cart does not contain an item for the specified variant.", CartErrors.CartItemNotFound.Description);
    }

    [Fact]
    public void InvalidMergeSource_HasStableCode()
    {
        Assert.Equal("Cart.InvalidMergeSource", CartErrors.InvalidMergeSource.Code);
        Assert.Equal("Only a guest cart can be used as the source of a cart merge.", CartErrors.InvalidMergeSource.Description);
    }

    [Fact]
    public void QuantityLimitExceeded_HasStableCode()
    {
        Assert.Equal("Cart.QuantityLimitExceeded", CartErrors.QuantityLimitExceeded.Code);
        Assert.Equal("The cart item quantity exceeds the maximum allowed.", CartErrors.QuantityLimitExceeded.Description);
    }

    [Fact]
    public void CartConcurrencyConflict_HasStableCode()
    {
        Assert.Equal("Cart.CartConcurrencyConflict", CartErrors.CartConcurrencyConflict.Code);
        Assert.Equal("The cart was modified by another request. Reload the cart and try again.", CartErrors.CartConcurrencyConflict.Description);
    }

    [Fact]
    public void CartNotFound_HasStableCode()
    {
        Assert.Equal("Cart.CartNotFound", CartErrors.CartNotFound.Code);
        Assert.Equal("The cart was not found.", CartErrors.CartNotFound.Description);
    }

    [Fact]
    public void CartAccessDenied_HasStableCode()
    {
        Assert.Equal("Cart.CartAccessDenied", CartErrors.CartAccessDenied.Code);
        Assert.Equal("The cart is not accessible with the provided identifier.", CartErrors.CartAccessDenied.Description);
    }

    [Fact]
    public void InvalidCartId_HasStableCode()
    {
        Assert.Equal("Cart.InvalidCartId", CartErrors.InvalidCartId.Code);
        Assert.Equal("A valid cart identifier is required.", CartErrors.InvalidCartId.Description);
    }

    [Fact]
    public void InvalidCartItem_HasStableCode()
    {
        Assert.Equal("Cart.InvalidCartItem", CartErrors.InvalidCartItem.Code);
        Assert.Equal("The cart item data is invalid.", CartErrors.InvalidCartItem.Description);
    }

    [Fact]
    public void AllErrors_HaveNonEmptyDescriptions()
    {
        Assert.All(AllErrors(), error => Assert.False(string.IsNullOrWhiteSpace(error.Description)));
    }

    [Fact]
    public void AllErrors_HaveModulePrefixedCodes()
    {
        Assert.All(AllErrors(), error => Assert.StartsWith("Cart.", error.Code));
    }

    [Fact]
    public void AllErrorCodes_AreUnique()
    {
        var codes = AllErrors().Select(error => error.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Errors_AreEqual_ToDomainErrorWithSameCodeAndDescription()
    {
        Assert.Equal(DomainError.Create("Cart.InvalidQuantity", "A cart item quantity must be a positive integer."), CartErrors.InvalidQuantity);
        Assert.Equal(DomainError.Create("Cart.QuantityLimitExceeded", "The cart item quantity exceeds the maximum allowed."), CartErrors.QuantityLimitExceeded);
        Assert.Equal(DomainError.Create("Cart.CartItemNotFound", "The cart does not contain an item for the specified variant."), CartErrors.CartItemNotFound);
        Assert.Equal(DomainError.Create("Cart.InvalidMergeSource", "Only a guest cart can be used as the source of a cart merge."), CartErrors.InvalidMergeSource);
        Assert.Equal(DomainError.Create("Cart.CartConcurrencyConflict", "The cart was modified by another request. Reload the cart and try again."), CartErrors.CartConcurrencyConflict);
        Assert.Equal(DomainError.Create("Cart.CartNotFound", "The cart was not found."), CartErrors.CartNotFound);
        Assert.Equal(DomainError.Create("Cart.CartAccessDenied", "The cart is not accessible with the provided identifier."), CartErrors.CartAccessDenied);
        Assert.Equal(DomainError.Create("Cart.InvalidCartId", "A valid cart identifier is required."), CartErrors.InvalidCartId);
        Assert.Equal(DomainError.Create("Cart.InvalidCartItem", "The cart item data is invalid."), CartErrors.InvalidCartItem);
    }

    [Fact]
    public void Errors_MapToApplicationError_ThroughSharedResultModel()
    {
        var error = Error.Conflict(CartErrors.InvalidQuantity.Code, CartErrors.InvalidQuantity.Description);

        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal(CartErrors.InvalidQuantity.Code, error.Code);
        Assert.Equal(CartErrors.InvalidQuantity.Description, error.Description);
    }

    [Fact]
    public void CartConcurrencyConflict_MapsToApplicationConflictError()
    {
        var error = Error.Conflict(CartErrors.CartConcurrencyConflict.Code, CartErrors.CartConcurrencyConflict.Description);

        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("Cart.CartConcurrencyConflict", error.Code);
        Assert.Equal(CartErrors.CartConcurrencyConflict.Description, error.Description);
    }

    private static IEnumerable<DomainError> AllErrors()
    {
        yield return CartErrors.InvalidQuantity;
        yield return CartErrors.QuantityLimitExceeded;
        yield return CartErrors.CartItemNotFound;
        yield return CartErrors.InvalidMergeSource;
        yield return CartErrors.CartConcurrencyConflict;
        yield return CartErrors.CartNotFound;
        yield return CartErrors.CartAccessDenied;
        yield return CartErrors.InvalidCartId;
        yield return CartErrors.InvalidCartItem;
    }
}
