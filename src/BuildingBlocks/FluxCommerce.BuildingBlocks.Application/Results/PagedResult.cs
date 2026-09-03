namespace FluxCommerce.BuildingBlocks.Application.Results;

public sealed record PagedResult<T>(
    IReadOnlyCollection<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
