using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;

/// <summary>
/// Retrieves the current authenticated user's active Cart. The UserId is resolved from
/// <see cref="ICurrentUser"/>; the query carries no Cart or User identifier. A user without an
/// active Cart follows the existing Cart-not-found behavior.
/// </summary>
public sealed record GetAuthenticatedCartQuery : IQuery<Result<CartState>>;
