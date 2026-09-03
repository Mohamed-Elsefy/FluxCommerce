using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;

namespace FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;

/// <summary>
/// Retrieves a Guest Cart by its opaque Guest Cart identifier.
/// </summary>
public sealed record GetGuestCartQuery(Guid CartId) : IQuery<Result<CartState>>;

