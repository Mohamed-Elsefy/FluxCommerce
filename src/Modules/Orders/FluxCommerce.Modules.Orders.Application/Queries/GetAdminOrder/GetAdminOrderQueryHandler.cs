using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Mapping;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrder;

/// <summary>
/// Loads the requested Order by identity alone (no customer scoping) and maps it
/// to the full response. A missing Order surfaces as not-found.
/// </summary>
public sealed class GetAdminOrderQueryHandler
    : IQueryHandler<GetAdminOrderQuery, Result<OrderDetailsResponse>>
{
    private readonly IOrderStore _orderStore;

    public GetAdminOrderQueryHandler(IOrderStore orderStore)
    {
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
    }

    public async Task<Result<OrderDetailsResponse>> HandleAsync(
        GetAdminOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await _orderStore
            .GetByIdAsync(new OrderId(query.OrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
            return Result.Failure<OrderDetailsResponse>(OrderQueryErrors.NotFound);

        return Result.Success(OrderResponseMapper.ToDetails(order));
    }
}
