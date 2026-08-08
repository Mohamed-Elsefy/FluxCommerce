using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;

namespace FluxCommerce.Modules.Cart.Application.Commands.RemoveGuestCartItem;

/// <summary>
/// Removes an item from a Guest Cart by its Variant identity. Missing items follow the
/// existing Cart Domain behavior.
/// </summary>
public sealed record RemoveGuestCartItemCommand(
    Guid CartId,
    Guid VariantId) : ICommand<Result<CartState>>;
