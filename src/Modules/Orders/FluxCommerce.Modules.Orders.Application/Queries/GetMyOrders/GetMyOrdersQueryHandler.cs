using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Mapping;
using FluxCommerce.Modules.Orders.Application.Validation;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetMyOrders;

/// <summary>
/// Resolves the authenticated caller, validates pagination and returns the caller's Orders from the
/// lightweight list projection.
/// </summary>
public sealed class GetMyOrdersQueryHandler : IQueryHandler<GetMyOrdersQuery, Result<PagedResult<OrderSummaryResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IOrderStore _orderStore;

    public GetMyOrdersQueryHandler(ICurrentUser currentUser, IOrderStore orderStore)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
    }

    public async Task<Result<PagedResult<OrderSummaryResponse>>> HandleAsync(
        GetMyOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result.Failure<PagedResult<OrderSummaryResponse>>(OrderQueryErrors.AuthenticationRequired);

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

        var result = await _orderStore
            .GetMyOrdersPageAsync(
                new CustomerId(_currentUser.UserId.Value),
                pageNumber,
                pageSize,
                status,
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new PagedResult<OrderSummaryResponse>(
            OrderResponseMapper.ToSummaries(result.Items),
            result.TotalCount,
            result.PageNumber,
            result.PageSize));
    }
}
