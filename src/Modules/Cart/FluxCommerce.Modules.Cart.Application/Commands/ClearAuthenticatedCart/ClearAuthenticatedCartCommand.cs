using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;

namespace FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;

/// <summary>
/// Removes every item from the current authenticated user's active Cart while preserving its
/// identity, User ownership, version semantics and persistence state.
/// </summary>
public sealed record ClearAuthenticatedCartCommand : ICommand<Result<CartState>>;

