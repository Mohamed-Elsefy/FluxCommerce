using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Commands.RequestOrderRefund;

/// <summary>
/// Requests a post-payment refund for any Order through the saga compensation flow, without
/// customer ownership scoping. The caller must be authorized at the API boundary
/// (<c>AdminPolicy</c>). Only a Completed (paid) saga is eligible; the refund itself is
/// processed asynchronously by the Payments module.
/// </summary>
public sealed record RequestOrderRefundCommand(Guid OrderId, string Reason)
    : ICommand<Result<OrderDetailsResponse>>;
