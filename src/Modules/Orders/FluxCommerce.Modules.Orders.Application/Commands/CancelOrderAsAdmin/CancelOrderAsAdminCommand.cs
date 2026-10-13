using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Contracts.Responses;

namespace FluxCommerce.Modules.Orders.Application.Commands.CancelOrderAsAdmin;

/// <summary>
/// Cancels any Order while it is still Pending, without customer ownership scoping.
/// The caller must be authorized at the API boundary (<c>AdminPolicy</c>). Finalized
/// Orders are rejected with a conflict; a Confirmed Order must go through the refund
/// flow instead.
/// </summary>
public sealed record CancelOrderAsAdminCommand(Guid OrderId)
    : ICommand<Result<OrderDetailsResponse>>;
