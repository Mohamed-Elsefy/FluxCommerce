using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.UpdateAuthenticatedCartItemQuantity;

/// <summary>
/// Updates the quantity of an item in the current authenticated user's active Cart. Quantity
/// validity is enforced by the Cart Domain invariant; the Application passes the requested quantity
/// through unchanged.
/// </summary>
public sealed record UpdateAuthenticatedCartItemQuantityCommand(
    Guid VariantId,
    int Quantity) : ICommand<Result<CartState>>;
