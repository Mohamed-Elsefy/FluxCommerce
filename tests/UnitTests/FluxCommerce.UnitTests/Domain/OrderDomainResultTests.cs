using FluxCommerce.Modules.Orders.Domain.Errors;
using FluxCommerce.Modules.Orders.Domain.Results;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class OrderDomainResultTests
{
    [Fact]
    public void Success_ReturnsSuccessResult_WithoutError()
    {
        var result = DomainResult.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(DomainError.None, result.Error);
    }

    [Fact]
    public void Failure_ReturnsFailureResult_WithError()
    {
        var error = DomainError.Create("Order.Test", "Test error");

        var result = DomainResult.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Failure_WithNoneError_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DomainResult.Failure(DomainError.None));
    }

    [Fact]
    public void SuccessGeneric_ReturnsValue()
    {
        var result = DomainResult.Success("value");

        Assert.True(result.IsSuccess);
        Assert.Equal("value", result.Value);
    }

    [Fact]
    public void FailureGeneric_ReturnsFailure_WithoutValue()
    {
        var error = DomainError.Create("Order.Test", "Test error");

        var result = DomainResult.Failure<string>(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void DomainError_Create_RequiresCodeAndDescription()
    {
        Assert.Throws<ArgumentException>(() => DomainError.Create("", "description"));
        Assert.Throws<ArgumentException>(() => DomainError.Create("Code", ""));
        Assert.Throws<ArgumentException>(() => DomainError.Create("  ", "description"));
    }

    [Fact]
    public void DomainError_None_IsSentinel()
    {
        Assert.Equal(string.Empty, DomainError.None.Code);
        Assert.Equal(string.Empty, DomainError.None.Description);
    }

    [Fact]
    public void DomainError_Equality_IsByCodeAndDescription()
    {
        var first = DomainError.Create("Order.Test", "Test error");
        var second = DomainError.Create("Order.Test", "Test error");

        Assert.Equal(first, second);
    }
}
