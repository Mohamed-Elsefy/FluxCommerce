using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;

/// <summary>
/// Creates the current authenticated user's active Cart. The UserId is resolved from
/// <see cref="ICurrentUser"/>; the request carries no Cart or User identifier. If the user already
/// has an active Cart it is returned unchanged (one active Cart per user).
/// </summary>
public sealed record CreateAuthenticatedCartCommand : ICommand<Result<CartState>>;
