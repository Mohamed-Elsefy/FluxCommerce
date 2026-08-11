using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Mapping;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Commands.CancelOrder;

/// <summary>
/// Cancels an Order scoped to the current customer. A Pending Order is transitioned and persisted;
/// an already-finalized Order is rejected with a conflict so a double-cancel is a stable, explicit
/// response rather than a silent no-op.
/// </summary>
public sealed class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand, Result<OrderDetailsResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOrderStore _orderStore;

    public CancelOrderCommandHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        IOrderStore orderStore)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
    }

    public async Task<Result<OrderDetailsResponse>> HandleAsync(
        CancelOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Result.Failure<OrderDetailsResponse>(OrderQueryErrors.AuthenticationRequired);

        var order = await _orderStore
            .GetOwnedOrderAsync(
                new CustomerId(_currentUser.UserId.Value),
                new OrderId(command.OrderId),
                cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
            return Result.Failure<OrderDetailsResponse>(OrderQueryErrors.NotFound);

        var cancellation = order.Cancel(new DateTimeOffset(_dateTimeProvider.UtcNow));
        if (cancellation.IsFailure)
            return Result.Failure<OrderDetailsResponse>(OrderQueryErrors.CancelConflict);

        await _orderStore.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(OrderResponseMapper.ToDetails(order));
    }
}
