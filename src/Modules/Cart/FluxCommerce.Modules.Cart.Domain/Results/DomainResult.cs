using FluxCommerce.Modules.Cart.Domain.Errors;

namespace FluxCommerce.Modules.Cart.Domain.Results;

public class DomainResult
{
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError Error { get; }

    protected DomainResult(bool isSuccess, DomainError error)
    {
        if (isSuccess && error != DomainError.None)
            throw new InvalidOperationException("A success result cannot contain an error.");

        if (!isSuccess && error == DomainError.None)
            throw new InvalidOperationException("A failure result must contain an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static DomainResult Success() => new(true, DomainError.None);

    public static DomainResult Failure(DomainError error) => new(false, error);

    public static DomainResult<T> Success<T>(T value) => DomainResult<T>.Success(value);

    public static DomainResult<T> Failure<T>(DomainError error) => DomainResult<T>.Failure(error);
}
