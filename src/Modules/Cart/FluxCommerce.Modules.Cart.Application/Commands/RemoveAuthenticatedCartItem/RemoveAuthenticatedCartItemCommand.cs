using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.RemoveAuthenticatedCartItem;

/// <summary>
/// Removes an item from the current authenticated user's active Cart by its Variant identity.
/// Missing items follow the existing Cart Domain behavior.
/// </summary>
public sealed record RemoveAuthenticatedCartItemCommand(
    Guid VariantId) : ICommand<Result<CartState>>;
