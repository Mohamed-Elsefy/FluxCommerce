using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.ReadModels;

namespace FluxCommerce.Modules.Orders.Application.Queries.GetOrderDailyPoints;

/// <summary>
/// Serves the Orders daily-series projection from a lightweight timestamp query. The trailing
/// window is anchored on the shared clock so the host buckets the same days it queried.
/// </summary>
public sealed class GetOrderDailyPointsQueryHandler
    : IQueryHandler<GetOrderDailyPointsQuery, Result<IReadOnlyList<OrderDailyPointReadModel>>>
{
    private readonly IOrderStore _orderStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetOrderDailyPointsQueryHandler(IOrderStore orderStore, IDateTimeProvider dateTimeProvider)
    {
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<IReadOnlyList<OrderDailyPointReadModel>>> HandleAsync(
        GetOrderDailyPointsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Days < 1)
            return Result.Failure<IReadOnlyList<OrderDailyPointReadModel>>(OrderQueryErrors.ValidationFailed);

        var from = new DateTimeOffset(_dateTimeProvider.UtcNow, TimeSpan.Zero).AddDays(-query.Days);

        var points = await _orderStore
            .GetCreatedPointsAsync(from, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(points);
    }
}
