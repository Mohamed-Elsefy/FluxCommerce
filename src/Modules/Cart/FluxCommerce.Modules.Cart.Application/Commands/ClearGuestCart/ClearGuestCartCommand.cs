using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;

namespace FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;

/// <summary>
/// Removes every item from an existing Guest Cart while preserving its identity, Guest
/// ownership, version semantics and persistence state.
/// </summary>
public sealed record ClearGuestCartCommand(Guid CartId) : ICommand<Result<CartState>>;

