using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrderCounts;

/// <summary>
/// Returns aggregate Order counts grouped by status for the administrative overview.
/// </summary>
public sealed class GetAdminOrderCountsQueryHandler
    : IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>>
{
    private readonly IOrderStore _orderStore;

    public GetAdminOrderCountsQueryHandler(IOrderStore orderStore)
    {
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
    }

    public async Task<Result<AdminOrderCountsResponse>> HandleAsync(
        GetAdminOrderCountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var counts = await _orderStore
            .GetAdminOrderCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new AdminOrderCountsResponse(
            counts.Total,
            counts.Pending,
            counts.Confirmed,
            counts.Cancelled));
    }
}
