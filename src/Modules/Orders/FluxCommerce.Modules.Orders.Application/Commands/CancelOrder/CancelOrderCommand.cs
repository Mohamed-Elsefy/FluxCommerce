using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Commands.CancelOrder;

/// <summary>
/// Cancels an Order owned by the authenticated customer when it is still in the Pending state.
/// </summary>
public sealed record CancelOrderCommand(Guid OrderId)
    : ICommand<Result<OrderDetailsResponse>>;
