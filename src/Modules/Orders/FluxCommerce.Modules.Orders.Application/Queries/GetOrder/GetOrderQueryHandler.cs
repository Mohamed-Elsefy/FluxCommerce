using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Mapping;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetOrder;

/// <summary>
/// Loads the requested Order scoped to the current customer and maps it to the full response. A
/// missing Order or one owned by another customer both surface as a not-found error.
/// </summary>
public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, Result<OrderDetailsResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IOrderStore _orderStore;

    public GetOrderQueryHandler(ICurrentUser currentUser, IOrderStore orderStore)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
    }

    public async Task<Result<OrderDetailsResponse>> HandleAsync(
        GetOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result.Failure<OrderDetailsResponse>(OrderQueryErrors.AuthenticationRequired);

        var order = await _orderStore
            .GetOwnedOrderAsync(
                new CustomerId(_currentUser.UserId.Value),
                new OrderId(query.OrderId),
                cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
            return Result.Failure<OrderDetailsResponse>(OrderQueryErrors.NotFound);

        return Result.Success(OrderResponseMapper.ToDetails(order));
    }
}
