using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;

/// <summary>
/// Updates the quantity of an item in a Guest Cart. Quantity validity is enforced by the
/// Cart Domain invariant; the Application passes the requested quantity through unchanged.
/// </summary>
public sealed record UpdateGuestCartItemQuantityCommand(
    Guid CartId,
    Guid VariantId,
    int Quantity) : ICommand<Result<CartState>>;
