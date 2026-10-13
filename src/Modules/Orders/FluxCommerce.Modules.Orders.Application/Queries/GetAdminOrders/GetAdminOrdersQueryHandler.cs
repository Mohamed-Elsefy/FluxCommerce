using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Mapping;
using FluxCommerce.Modules.Orders.Application.Validation;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.Enums;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrders;

/// <summary>
/// Administrative Order list: validates pagination and the optional status filter,
/// then reads from the lightweight list projection without ownership scoping.
/// </summary>
public sealed class GetAdminOrdersQueryHandler
    : IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>>
{
    private readonly IOrderStore _orderStore;

    public GetAdminOrdersQueryHandler(IOrderStore orderStore)
    {
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
    }

    public async Task<Result<PagedResult<OrderSummaryResponse>>> HandleAsync(
        GetAdminOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageNumber = query.PageNumber ?? OrderPaginationRules.DefaultPageNumber;
        var pageSize = query.PageSize ?? OrderPaginationRules.DefaultPageSize;

        var validationError = OrderPaginationRules.Validate(pageNumber, pageSize);
        if (validationError is not null)
            return Result.Failure<PagedResult<OrderSummaryResponse>>(validationError);

        OrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<OrderStatus>(query.Status.Trim(), ignoreCase: true, out var parsed)
                || !Enum.IsDefined(typeof(OrderStatus), parsed))
                return Result.Failure<PagedResult<OrderSummaryResponse>>(OrderQueryErrors.ValidationFailed);

            status = parsed;
        }

        if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
            return Result.Failure<PagedResult<OrderSummaryResponse>>(OrderQueryErrors.ValidationFailed);

        var result = await _orderStore
            .GetAdminOrdersPageAsync(
                status,
                query.CustomerId,
                query.Search,
                query.From,
                query.To,
                pageNumber,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new PagedResult<OrderSummaryResponse>(
            OrderResponseMapper.ToSummaries(result.Items),
            result.TotalCount,
            result.PageNumber,
            result.PageSize));
    }
}
