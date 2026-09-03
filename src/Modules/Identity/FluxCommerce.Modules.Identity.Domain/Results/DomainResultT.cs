using FluxCommerce.Modules.Identity.Domain.Errors;

namespace FluxCommerce.Modules.Identity.Domain.Results;

public class DomainResult<T> : DomainResult
{
    public T Value { get; }

    private DomainResult(T value) : base(true, DomainError.None)
    {
        Value = value;
    }

    private DomainResult(DomainError error) : base(false, error)
    {
        Value = default!;
    }

    public static DomainResult<T> Success(T value) => new(value);

    public new static DomainResult<T> Failure(DomainError error) => new(error);
}
