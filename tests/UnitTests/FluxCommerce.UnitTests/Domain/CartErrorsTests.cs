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
        Assert.Equal(DomainError.Create("Cart.CartItemNotFound", "The cart does not contain an item for the specified variant."), CartErrors.CartItemNotFound);
        Assert.Equal(DomainError.Create("Cart.InvalidMergeSource", "Only a guest cart can be used as the source of a cart merge."), CartErrors.InvalidMergeSource);
    }

    [Fact]
    public void Errors_MapToApplicationError_ThroughSharedResultModel()
    {
        var error = Error.Conflict(CartErrors.InvalidQuantity.Code, CartErrors.InvalidQuantity.Description);

        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal(CartErrors.InvalidQuantity.Code, error.Code);
        Assert.Equal(CartErrors.InvalidQuantity.Description, error.Description);
    }

    private static IEnumerable<DomainError> AllErrors()
    {
        yield return CartErrors.InvalidQuantity;
        yield return CartErrors.CartItemNotFound;
        yield return CartErrors.InvalidMergeSource;
    }
}
