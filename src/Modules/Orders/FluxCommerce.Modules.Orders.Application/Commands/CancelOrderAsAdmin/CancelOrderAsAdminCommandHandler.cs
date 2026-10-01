using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Mapping;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Commands.CancelOrderAsAdmin;

/// <summary>
/// Cancels any Pending Order by identity alone. The OrderCancelled domain event flows
/// through the Outbox, so the saga compensation (inventory release) runs exactly as it
/// does for the customer-initiated cancellation.
/// </summary>
public sealed class CancelOrderAsAdminCommandHandler
    : ICommandHandler<CancelOrderAsAdminCommand, Result<OrderDetailsResponse>>
{
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOrderStore _orderStore;

    public CancelOrderAsAdminCommandHandler(
        IDateTimeProvider dateTimeProvider,
        IOrderStore orderStore)
    {
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
    }

    public async Task<Result<OrderDetailsResponse>> HandleAsync(
        CancelOrderAsAdminCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await _orderStore
            .GetByIdAsync(new OrderId(command.OrderId), cancellationToken)
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
