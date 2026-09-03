using FluxCommerce.BuildingBlocks.Application.Errors;

namespace FluxCommerce.BuildingBlocks.Application.Results;

public class Result<T> : Result
{
    public T Value { get; }

    private Result(T value) : base(true, Error.None)
    {
        Value = value;
    }

    private Result(Error error) : base(false, error)
    {
        Value = default!;
    }

    public static Result<T> Success(T value) => new(value);

    public new static Result<T> Failure(Error error) => new(error);
}
