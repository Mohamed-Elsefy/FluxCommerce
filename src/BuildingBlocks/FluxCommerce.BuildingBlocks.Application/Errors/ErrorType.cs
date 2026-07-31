namespace FluxCommerce.BuildingBlocks.Application.Errors;

public enum ErrorType
{
    None = 0,
    NotFound = 1,
    Validation = 2,
    Conflict = 3,
    Failure = 4,
    Unauthorized = 5,
    Forbidden = 6,
}
